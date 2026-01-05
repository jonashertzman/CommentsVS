using CommentsVS.Options;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to open the extension's settings page.
    /// </summary>
    [Command(PackageIds.OpenSettings)]
    internal sealed class OpenSettingsCommand : BaseCommand<OpenSettingsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await VS.Settings.OpenAsync<OptionsProvider.GeneralOptions>();
        }
    }
}

