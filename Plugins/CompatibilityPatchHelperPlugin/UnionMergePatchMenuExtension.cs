using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Windows;
using System.Linq;

namespace CompatibilityPatchHelperPlugin
{
    internal class UnionMergePatchMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName { get; } = "File";
        public override string MenuItemName { get; } = "Union Merge Two Projects' EBX List Assets";


        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {

            // no multiselect, this works with two files only, no less or more!

            if (App.AssetManager.GetDirtyCount() > 0)
            {
                App.Logger.LogError("Union merge only works from empty projects!");
                return;
            }

            FrostyOpenFileDialog openFileDialog = new FrostyOpenFileDialog("Select the first project file", "Project File (*.fbproject)|*.fbproject", "FrostyProject");
            if (!openFileDialog.ShowDialog()) return;

            string firstFileName = openFileDialog.FileName;

            openFileDialog = new FrostyOpenFileDialog("Select the second project file", "Project File (*.fbproject)|*.fbproject", "FrostyProject");
            if (!openFileDialog.ShowDialog()) return;

            string secondFileName = openFileDialog.FileName;

            if (firstFileName.Equals(secondFileName))
            {
                App.Logger.Log("Cannot merge a file with itself!");
                return;
            }

            StartUnionMerge(firstFileName, secondFileName);

            App.EditorWindow.DataExplorer.ShowOnlyModified = true;
        });

        private void StartUnionMerge(string projectName1, string projectName2)
        {

            FrostyTaskWindow.Show("Union merging projects...", "", task =>
            {
                task.Update("loading first project...");
                var dataFromFirstProject = LimitedProjectLoader.LoadFromProject(projectName1);

                if (dataFromFirstProject.Count == 0)
                {
                    App.Logger.Log("Project <{0}> contains no mergeable ebx edits!", projectName1);
                    return;
                }

                var ebxNames = dataFromFirstProject.Keys.ToHashSet();

                task.Update($"loading second project, limited to {ebxNames.Count} ebx from the first...", 33d);
                var dataFromSecondProject = LimitedProjectLoader.LoadFromProject(projectName2, true, ebxNames);

                if (dataFromSecondProject.Count == 0)
                {
                    App.Logger.Log("Project <{0}> contains no ebx edits that overlap with the first project!", projectName2);
                    return;
                }

                App.Logger.Log("Project <{0}> contains <{1}> ebx edit(s) that overlap with the first project!", projectName2, dataFromSecondProject.Count);

                task.Update("begin union merging assets...", 66d);

                foreach (var entry in dataFromSecondProject)
                {
                    string assetName = entry.Key;
                    AssetModification firstProjectEntry = dataFromFirstProject[assetName];
                    AssetModification secondProjectEntry = entry.Value;

                    // only try merging if both ebx actually exist:
                    if (firstProjectEntry.Ebx != null && secondProjectEntry.Ebx != null)
                    {

                        App.Logger.Log("...trying to merge ebx <{0}>", assetName);
                        CompatibilityPatchHelper.MergeAssets(firstProjectEntry, secondProjectEntry);
                    }
                    else
                    {
                        App.Logger.LogWarning("Same Ebx <{0}> of both projects had the first ebx == null: <{1}> | second ebx == null: <{2}>", assetName, firstProjectEntry.Ebx == null, secondProjectEntry.Ebx == null);
                    }
                }

                App.Logger.Log("Done union merging projects <{0}> and <{1}> into the current project", projectName1, projectName2);

            });
        }
    }
}
