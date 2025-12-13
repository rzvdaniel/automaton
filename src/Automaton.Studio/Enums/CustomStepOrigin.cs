namespace Automaton.Studio.Enums;

/// <summary>
/// Indicates how a custom step was created
/// </summary>
public enum CustomStepOrigin
{
    /// <summary>
    /// Created manually by user via StepsPage
    /// </summary>
    Manual = 0,

    /// <summary>
    /// Created by AI via WebApp/AppPage
    /// </summary>
    AppGenerated = 1
}
