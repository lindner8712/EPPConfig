namespace EppConfig.Services;

public class BlocklyStateService
{
    private string _xml = string.Empty;

    public string Xml => _xml;

    public event Action<string>? XmlChanged;

    public void SetXml(string xml)
    {
        var value = xml ?? string.Empty;
        if (value == _xml)
        {
            return;
        }

        _xml = value;
        XmlChanged?.Invoke(_xml);
    }
}
