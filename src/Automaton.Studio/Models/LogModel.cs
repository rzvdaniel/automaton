using AntDesign;
using Automaton.Core.Enums;
using Newtonsoft.Json;

namespace Automaton.Studio.Models;

public class LogModel
{
    public int Id { get; set; }

    public string Level { get; set; }

    public string Message { get; set; }

    public string MessageTemplate { get; set; }

    public string Exception { get; set; }

    public string Properties { get; set; }

    public DateTime Timestamp { get; set; }

    public string EventType { get; set; }

    public string UserName { get; set; }

    public Dictionary<string, object> PropertiesDictionary
    {
        get { return JsonConvert.DeserializeObject<Dictionary<string, object>>(Properties); }
    }

    public BadgeStatus LevelClass => LevelClasses[Level];

    private readonly Dictionary<string, BadgeStatus> LevelClasses = new()
    {
        { "0", BadgeStatus.Default },
        { "1", BadgeStatus.Default },
        { "2", BadgeStatus.Success },
        { "3", BadgeStatus.Warning },
        { "4", BadgeStatus.Error },
        { "5", BadgeStatus.Error },
    };
}
