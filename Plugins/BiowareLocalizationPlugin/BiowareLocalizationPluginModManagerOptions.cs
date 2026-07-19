
using Frosty.Core;
using FrostySdk.Attributes;

namespace BiowareLocalizationPlugin
{
    [DisplayName("Bioware Localization Options")]
    public class BiowareLocalizationPluginOptions : OptionsExtension
    {

        // The name for the global mod manager variable.
        public static readonly string SHOW_INDIVIDUAL_TEXTIDS_OPTION_NAME = "BwLoMoShowIndividualTextIds";

        // The name of the option to enable exporting everything in a resource to xml.
        public static readonly string ASK_XML_EXPORT_OPTIONS = "BwLoEoAskXmlExportOptions";

        // The name of the option to enable verification text printing.
        public static readonly string PRINT_VERIFICATION_TEXTS = "BwLocalizationPlugin.PrintVerificationTexts";

        // The name of the option to keep the original huffman encoding if possible.
        public static readonly string KEEP_ORIGINAL_ENCODING_IF_POSSIBLE = "BwLocalizationPlugin.KeepOriginalHuffmanTreeIfPossible";

        [Category("Mod Manager Options")]
        [DisplayName("Show Individual Text Ids")]
        [Description("If enabled, all individual text ids in each resource (res) are shown in the mod manager's Actions tab. Otherwise only the resource iteself is shown as merged. This setting is only for the mod manager and has no effect in the editor. This is a global setting.")]
        [EbxFieldMeta(FrostySdk.IO.EbxFieldType.Boolean)]
        public bool ShowIndividualTextIds { get; set; } = false;

        [Category("Editor Options")]
        [DisplayName("Ask for Xml Export Options")]
        [Description("If enabled, a popup prompt allows selecting whether to export all texts or only modified ones. If this value is false, then the default from below is used. This setting is only for the editor and has no effect in the mod manager. This is a global setting.")]
        [EbxFieldMeta(FrostySdk.IO.EbxFieldType.Boolean)]
        public bool AskForXmlExportOptions { get; set; } = false;

        [Category("General Options")]
        [DisplayName("Use original encoding")]
        [Description("Tries to reuse the original text encoding whenever possible. If an altered texts includes characters not included in the original encoding, then the newly created encoding will be used. This is a game specific setting.")]
        [EbxFieldMeta(FrostySdk.IO.EbxFieldType.Boolean)]
        public bool KeepOriginalEncodingIfPossible { get; set; } = false;

        [Category("General Options")]
        [DisplayName("Log verification texts")]
        [Description("If enabled, a lot of verification log entries will be created for each loaded and editet text resources. Should only be enabled to try and debug issues. This is a game specific setting.")]
        [EbxFieldMeta(FrostySdk.IO.EbxFieldType.Boolean)]
        public bool PrintVerificationTexts { get; set; } = false;

        public override void Load()
        {
            // mod manager
            ShowIndividualTextIds = Config.Get(SHOW_INDIVIDUAL_TEXTIDS_OPTION_NAME, false, ConfigScope.Global);

            // editor
            AskForXmlExportOptions = Config.Get(ASK_XML_EXPORT_OPTIONS, false, ConfigScope.Global);

            // both
            KeepOriginalEncodingIfPossible = Config.Get(KEEP_ORIGINAL_ENCODING_IF_POSSIBLE, false, ConfigScope.Game);
            PrintVerificationTexts = Config.Get(PRINT_VERIFICATION_TEXTS, false, ConfigScope.Game);
        }

        public override void Save()
        {
            // mod manager
            Config.Add(SHOW_INDIVIDUAL_TEXTIDS_OPTION_NAME, ShowIndividualTextIds, ConfigScope.Global);

            // editor
            Config.Add(ASK_XML_EXPORT_OPTIONS, AskForXmlExportOptions, ConfigScope.Global);

            // both
            Config.Add(KEEP_ORIGINAL_ENCODING_IF_POSSIBLE, KeepOriginalEncodingIfPossible, ConfigScope.Game);
            Config.Add(PRINT_VERIFICATION_TEXTS, PrintVerificationTexts, ConfigScope.Game);
        }
    }
}
