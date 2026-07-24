using System.Globalization;
using System.Xml.Linq;

namespace EppConfig.Services;

public enum LampColor
{
    None,
    Red,
    Green,
    Yellow,
    Blue
}

public class BlocklyStateService
{
    private string _xml = string.Empty;
    private readonly List<EthercatMasterNode> _masters = [];
    private readonly Dictionary<string, BlocklyNode> _blockIndex = new(StringComparer.Ordinal);
    private readonly PersistentLogService _log;

    public BlocklyStateService(PersistentLogService log)
    {
        _log = log;
    }

    public string Xml => _xml;
    public IReadOnlyList<EthercatMasterNode> Masters => _masters;

    public event Action<string>? XmlChanged;
    public event Action? ModelChanged;

    public void SetXml(string xml, string source = "Workspace")
    {
        var value = xml ?? string.Empty;
        if (value == _xml)
        {
            return;
        }

        var before = CreateSnapshotMap(_masters);

        _xml = value;
        RebuildModel();

        var after = CreateSnapshotMap(_masters);
        LogDiff(before, after, source);

        XmlChanged?.Invoke(_xml);
        ModelChanged?.Invoke();
    }

    public bool TrySetLamp(string blockId, LampColor color)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return false;
        }

        if (!_blockIndex.TryGetValue(blockId, out var block))
        {
            return false;
        }

        block.Lamp = color;
        _log.Log("Info", "Lamp", $"Lampenfarbe geändert: {color}", block.Type, block.Id, block.Bmk);
        ModelChanged?.Invoke();
        return true;
    }

    public LampColor? GetLamp(string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return null;
        }

        return _blockIndex.TryGetValue(blockId, out var block)
            ? block.Lamp
            : null;
    }

    public BlocklyNode? GetBlock(string blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return null;
        }

        return _blockIndex.TryGetValue(blockId, out var block)
            ? block
            : null;
    }

    private void RebuildModel()
    {
        _masters.Clear();
        _blockIndex.Clear();

        if (string.IsNullOrWhiteSpace(_xml))
        {
            return;
        }

        try
        {
            var document = XDocument.Parse(_xml);
            var root = document.Root;
            if (root is null)
            {
                return;
            }

            foreach (var blockElement in root.Elements("block"))
            {
                var master = ParseEthercatMaster(blockElement);
                if (master is null)
                {
                    continue;
                }

                _masters.Add(master);
                IndexBlockTree(master);
            }
        }
        catch
        {
            _masters.Clear();
            _blockIndex.Clear();
            _log.Log("Warning", "BlocklyState", "Blockly-XML konnte nicht geparst werden.");
        }
    }

    private void LogDiff(
        Dictionary<string, BlockSnapshot> before,
        Dictionary<string, BlockSnapshot> after,
        string source)
    {
        foreach (var added in after.Values.Where(a => !before.ContainsKey(a.Id)))
        {
            _log.Log("Info", source, "Block hinzugefügt", added.Type, added.Id, added.Bmk);
        }

        foreach (var removed in before.Values.Where(b => !after.ContainsKey(b.Id)))
        {
            _log.Log("Info", source, "Block entfernt", removed.Type, removed.Id, removed.Bmk);
        }

        foreach (var current in after.Values)
        {
            if (!before.TryGetValue(current.Id, out var previous))
            {
                continue;
            }

            if (!string.Equals(previous.Path, current.Path, StringComparison.Ordinal))
            {
                _log.Log("Info", source, "Reihenfolge/Position geändert", current.Type, current.Id, current.Bmk);
            }

            if (!string.Equals(previous.Bmk, current.Bmk, StringComparison.Ordinal))
            {
                _log.Log("Info", source, $"BMK geändert: '{previous.Bmk}' -> '{current.Bmk}'", current.Type, current.Id, current.Bmk);
            }

            if (previous.Value != current.Value)
            {
                _log.Log("Info", source, $"Wert geändert: {previous.Value} -> {current.Value}", current.Type, current.Id, current.Bmk);
            }

            if (!string.Equals(previous.AmsNetId, current.AmsNetId, StringComparison.Ordinal)
                || !string.Equals(previous.Adapter, current.Adapter, StringComparison.Ordinal))
            {
                _log.Log("Info", source, "Master-Felder geändert (AMS Net ID/Adapter)", current.Type, current.Id, current.Bmk);
            }
        }
    }

    private static Dictionary<string, BlockSnapshot> CreateSnapshotMap(IEnumerable<EthercatMasterNode> masters)
    {
        var map = new Dictionary<string, BlockSnapshot>(StringComparer.Ordinal);
        var masterIndex = 0;

        foreach (var master in masters)
        {
            var masterPath = $"M:{masterIndex}";
            map[master.Id] = new BlockSnapshot(master.Id, master.Type, master.Bmk, null, masterPath, master.AmsNetId, master.Adapter);

            for (var childIndex = 0; childIndex < master.Children.Count; childIndex++)
            {
                var child = master.Children[childIndex];
                var childPath = $"{masterPath}/C:{childIndex}";
                map[child.Id] = new BlockSnapshot(child.Id, child.Type, child.Bmk, null, childPath, null, null);

                for (var i = 0; i < child.Channel1.Count; i++)
                {
                    var module = child.Channel1[i];
                    map[module.Id] = new BlockSnapshot(module.Id, module.Type, module.Bmk, module.Value, $"{childPath}/CH1:{i}", null, null);
                }

                for (var i = 0; i < child.Channel2.Count; i++)
                {
                    var module = child.Channel2[i];
                    map[module.Id] = new BlockSnapshot(module.Id, module.Type, module.Bmk, module.Value, $"{childPath}/CH2:{i}", null, null);
                }
            }

            masterIndex++;
        }

        return map;
    }

    private EthercatMasterNode? ParseEthercatMaster(XElement blockElement)
    {
        if (!IsType(blockElement, "ethercat_master"))
        {
            return null;
        }

        var master = new EthercatMasterNode
        {
            Id = GetId(blockElement),
            Type = "ethercat_master",
            Bmk = GetFieldValue(blockElement, "BMK"),
            AmsNetId = GetFieldValue(blockElement, "AMS_NET_ID"),
            Adapter = GetFieldValue(blockElement, "ADAPTER")
        };

        var firstChild = GetStatementFirstBlock(blockElement, "CHILDREN");
        foreach (var child in ParseBlockChain(firstChild, ParseEpp1322))
        {
            master.Children.Add(child);
        }

        return master;
    }

    private Epp1322Node? ParseEpp1322(XElement blockElement)
    {
        if (!IsType(blockElement, "beckhoff_epp1322_0001"))
        {
            return null;
        }

        var node = new Epp1322Node
        {
            Id = GetId(blockElement),
            Type = "beckhoff_epp1322_0001",
            Bmk = GetFieldValue(blockElement, "BMK")
        };

        var firstChannel1Block = GetStatementFirstBlock(blockElement, "CHANNEL_1");
        foreach (var module in ParseBlockChain(firstChannel1Block, ParseModule))
        {
            node.Channel1.Add(module);
        }

        var firstChannel2Block = GetStatementFirstBlock(blockElement, "CHANNEL_2");
        foreach (var module in ParseBlockChain(firstChannel2Block, ParseModule))
        {
            node.Channel2.Add(module);
        }

        return node;
    }

    private ModuleNode? ParseModule(XElement blockElement)
    {
        var type = GetType(blockElement);
        if (type is not "beckhoff_epp1809_0021" and not "beckhoff_epp2316_0008")
        {
            return null;
        }

        return new ModuleNode
        {
            Id = GetId(blockElement),
            Type = type,
            Bmk = GetFieldValue(blockElement, "BMK"),
            Value = ParseDecimal(GetFieldValue(blockElement, "VALUE"))
        };
    }

    private static IEnumerable<TNode> ParseBlockChain<TNode>(XElement? firstBlock, Func<XElement, TNode?> parser)
        where TNode : BlocklyNode
    {
        var current = firstBlock;
        while (current is not null)
        {
            var parsed = parser(current);
            if (parsed is not null)
            {
                yield return parsed;
            }

            current = current.Element("next")?.Element("block");
        }
    }

    private void IndexBlockTree(EthercatMasterNode master)
    {
        _blockIndex[master.Id] = master;

        foreach (var child in master.Children)
        {
            _blockIndex[child.Id] = child;

            foreach (var module in child.Channel1)
            {
                _blockIndex[module.Id] = module;
            }

            foreach (var module in child.Channel2)
            {
                _blockIndex[module.Id] = module;
            }
        }
    }

    private static bool IsType(XElement blockElement, string expected)
        => string.Equals(GetType(blockElement), expected, StringComparison.Ordinal);

    private static string GetType(XElement blockElement)
        => blockElement.Attribute("type")?.Value ?? string.Empty;

    private static string GetId(XElement blockElement)
        => blockElement.Attribute("id")?.Value ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static string GetFieldValue(XElement blockElement, string fieldName)
        => blockElement
            .Elements("field")
            .FirstOrDefault(field => string.Equals(field.Attribute("name")?.Value, fieldName, StringComparison.Ordinal))
            ?.Value
            ?? string.Empty;

    private static XElement? GetStatementFirstBlock(XElement blockElement, string statementName)
        => blockElement
            .Elements("statement")
            .FirstOrDefault(statement => string.Equals(statement.Attribute("name")?.Value, statementName, StringComparison.Ordinal))
            ?.Element("block");

    private static decimal ParseDecimal(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentCulture))
        {
            return currentCulture;
        }

        return 0m;
    }

    private sealed record BlockSnapshot(
        string Id,
        string Type,
        string Bmk,
        decimal? Value,
        string Path,
        string? AmsNetId,
        string? Adapter);
}

public abstract class BlocklyNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string Bmk { get; init; } = string.Empty;
    public LampColor Lamp { get; set; } = LampColor.None;
}

public sealed class EthercatMasterNode : BlocklyNode
{
    public string AmsNetId { get; init; } = string.Empty;
    public string Adapter { get; init; } = string.Empty;
    public List<Epp1322Node> Children { get; } = [];
}

public sealed class Epp1322Node : BlocklyNode
{
    public List<ModuleNode> Channel1 { get; } = [];
    public List<ModuleNode> Channel2 { get; } = [];
}

public sealed class ModuleNode : BlocklyNode
{
    public decimal Value { get; init; }
}
