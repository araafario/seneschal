using Seneschal.Shell;

namespace Seneschal.Core;

/// <summary>
/// Puts Seneschal's own settings back to the defaults, at once and without a restart: the theme and sidebar, the icons, the favorites,
/// the assistant's settings and which extensions are switched off. Extensions, their data, projects, chats and the MCP secret are not
/// settings and stay. Each setting clears itself and tells whatever shows it, so the app redraws with the defaults.
/// </summary>
public sealed class SettingsReset(UiSettings ui, GroupIcons icons, Favorites favorites, AssistantSettings assistant, ExtensionSettings extensions)
{
    public void Run()
    {
        ui.ResetToDefaults();
        icons.ResetToDefaults();
        favorites.ResetToDefaults();
        assistant.ResetToDefaults();
        extensions.ResetToDefaults();
    }
}
