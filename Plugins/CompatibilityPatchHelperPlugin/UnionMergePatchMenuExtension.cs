using Frosty.Core;
using Frosty.Core.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompatibilityPatchHelperPlugin
{
    internal class UnionMergePatchMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName { get; } = "File";
        public override string MenuItemName { get; } = "Union Merge Two Projects' EBX List Assets";


        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {

            // no multiselect, this works with two files only, no less or more!

            if(App.AssetManager.GetDirtyCount() > 0)
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
        });
    }
}
