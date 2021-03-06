using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitCommands.Remotes;
using GitUIPluginInterfaces;
using GitUIPluginInterfaces.BuildServerIntegration;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages
{
    public partial class BuildServerIntegrationSettingsPage : RepoDistSettingsPage
    {
        private readonly TranslationString _noneItem =
            new TranslationString("None");
        private IConfigFileRemoteSettingsManager? _remotesManager;
        private JoinableTask<object>? _populateBuildServerTypeTask;

        public BuildServerIntegrationSettingsPage()
        {
            InitializeComponent();
            Text = "Build server integration";
            InitializeComplete();
        }

        protected override void Init(ISettingsPageHost pageHost)
        {
            base.Init(pageHost);

            _remotesManager = new ConfigFileRemoteSettingsManager(() => Module);
            _populateBuildServerTypeTask = ThreadHelper.JoinableTaskFactory.RunAsync(
                async () =>
                {
                    await TaskScheduler.Default.SwitchTo(alwaysYield: true);

                    var exports = ManagedExtensibility.GetExports<IBuildServerAdapter, IBuildServerTypeMetadata>();
                    var buildServerTypes = exports.Select(export =>
                        {
                            var canBeLoaded = export.Metadata.CanBeLoaded;
                            return export.Metadata.BuildServerType.Combine(" - ", canBeLoaded);
                        }).ToArray();

                    await this.SwitchToMainThreadAsync();

                    checkBoxEnableBuildServerIntegration.Enabled = true;
                    checkBoxShowBuildResultPage.Enabled = true;
                    BuildServerType.Enabled = true;

                    BuildServerType.DataSource = new[] { _noneItem.Text }.Concat(buildServerTypes).ToArray();
                    return BuildServerType.DataSource;
                });
        }

        public override bool IsInstantSavePage => false;

        protected override void SettingsToPage()
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(
                async () =>
                {
                    Validates.NotNull(_populateBuildServerTypeTask);
                    Validates.NotNull(CurrentSettings);

                    await _populateBuildServerTypeTask.JoinAsync();

                    await this.SwitchToMainThreadAsync();

                    checkBoxEnableBuildServerIntegration.SetNullableChecked(CurrentSettings.BuildServer.EnableIntegration.Value);
                    checkBoxShowBuildResultPage.SetNullableChecked(CurrentSettings.BuildServer.ShowBuildResultPage.Value);

                    BuildServerType.SelectedItem = CurrentSettings.BuildServer.Type.Value ?? _noneItem.Text;
                });
        }

        protected override void PageToSettings()
        {
            Validates.NotNull(CurrentSettings);

            CurrentSettings.BuildServer.EnableIntegration.Value = checkBoxEnableBuildServerIntegration.Checked;
            CurrentSettings.BuildServer.ShowBuildResultPage.Value = checkBoxShowBuildResultPage.Checked;

            var selectedBuildServerType = GetSelectedBuildServerType();

            CurrentSettings.BuildServer.Type.Value = selectedBuildServerType;

            var control =
                buildServerSettingsPanel.Controls.OfType<IBuildServerSettingsUserControl>()
                                        .SingleOrDefault();
            control?.SaveSettings(CurrentSettings.BuildServer.TypeSettings);
        }

        private void ActivateBuildServerSettingsControl()
        {
            Validates.NotNull(CurrentSettings);

            var controls = buildServerSettingsPanel.Controls.OfType<IBuildServerSettingsUserControl>().Cast<Control>();
            var previousControl = controls.SingleOrDefault();
            previousControl?.Dispose();

            var control = CreateBuildServerSettingsUserControl();

            buildServerSettingsPanel.Controls.Clear();

            if (control is not null)
            {
                control.LoadSettings(CurrentSettings.BuildServer.TypeSettings);

                buildServerSettingsPanel.Controls.Add((Control)control);
                ((Control)control).Dock = DockStyle.Fill;
            }
        }

        private IBuildServerSettingsUserControl? CreateBuildServerSettingsUserControl()
        {
            Validates.NotNull(Module);

            if (BuildServerType.SelectedIndex == 0 || string.IsNullOrEmpty(Module.WorkingDir))
            {
                return null;
            }

            var defaultProjectName = Module.WorkingDir.Split(Delimiters.PathSeparators, StringSplitOptions.RemoveEmptyEntries).Last();

            var exports = ManagedExtensibility.GetExports<IBuildServerSettingsUserControl, IBuildServerTypeMetadata>();
            var selectedExport = exports.SingleOrDefault(export => export.Metadata.BuildServerType == GetSelectedBuildServerType());
            if (selectedExport is not null)
            {
                var buildServerSettingsUserControl = selectedExport.Value;
                Validates.NotNull(_remotesManager);
                var remoteUrls = _remotesManager.LoadRemotes(false).Select(r => string.IsNullOrEmpty(r.PushUrl) ? r.Url : r.PushUrl);

                buildServerSettingsUserControl.Initialize(defaultProjectName, remoteUrls);
                return buildServerSettingsUserControl;
            }

            return null;
        }

        private string? GetSelectedBuildServerType()
        {
            if (BuildServerType.SelectedIndex == 0)
            {
                return null;
            }

            return (string)BuildServerType.SelectedItem;
        }

        private void BuildServerType_SelectedIndexChanged(object sender, EventArgs e)
        {
            ActivateBuildServerSettingsControl();
        }
    }
}
