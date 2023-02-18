using Frosty.Core;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System;
using Microsoft.SqlServer.Server;
using System.Net.NetworkInformation;
using System.Windows.Input;

namespace BwPlainStringLocalizationPlugin.LocalizedResources
{

    /// <summary>
    /// Implementation of the LocalizedStringResource - Resource type.
    /// Please note that there is another implementation with the same name for those resources used in MEA and DAI in BiowareLocalizationPlugin.LocalizedResources!
    /// </summary>
    public class LocalizedStringResource : Resource
    {

        #region string definition class
        internal class LocalizedString
        {

            public readonly uint Id;

            public readonly int DefaultPosition;
            public string Value { get; set; }

            public LocalizedString(uint inId, int inDefaultPosition, string inText)
            {
                Id = inId;
                DefaultPosition = inDefaultPosition;
                Value = inText;
            }
        }

        #endregion

        /// <summary>
        /// The (display) name of the resource this belongs to
        /// </summary>
        public string Name { get; private set; } = "not_yet_initialized";

        /// <summary>
        /// Event handler to be informed whenever the state of the modified resource changes drastically.
        /// </summary>
        public event EventHandler ResourceEventHandlers;

        /// <summary>
        /// The default texts
        /// </summary>
        private readonly List<LocalizedString> m_localizedStrings = new List<LocalizedString>();

        /// <summary>
        /// If any text is altered, the altered text entry will be kept in the modfiedResource.
        /// </summary>
        private ModifiedPlainLocalizationResource m_modifiedResource = null;

        public LocalizedStringResource()
        {
            // nothing to do.
        }

        public override void Read(NativeReader reader, AssetManager am, ResAssetEntry entry, ModifiedResource modifiedData)
        {

            base.Read(reader, am, entry, modifiedData);

            Name = new StringBuilder(entry.Filename)
                .Append(" - ")
                .Append(entry.Name)
                .ToString();

            int gameProfile = ProfilesLibrary.DataVersion;

            if((int)ProfileVersion.Anthem != gameProfile && (int)ProfileVersion.DeadSpace != gameProfile)
            {
                throw new InvalidOperationException("BwPlainStringLocalizationPlugin currenlty only supports Anthem and DeadSpace!");
            }

            // actual reading starts here:
            _ = reader.ReadUInt();
            _ = reader.ReadUInt();
            _ = reader.ReadUInt();

            long numStrings = reader.ReadLong();
            reader.Position += 0x18;

            Dictionary<uint, List<uint>> hashToStringIdMapping = new Dictionary<uint, List<uint>>();

            for (int i = 0; i < numStrings; i++)
            {
                uint hash = reader.ReadUInt();
                uint stringId = reader.ReadUInt();
                reader.Position += 8;
                if (!hashToStringIdMapping.ContainsKey(hash))
                    hashToStringIdMapping.Add(hash, new List<uint>());
                hashToStringIdMapping[hash].Add(stringId);
            }

            reader.Position += 0x18;

            while (reader.Position < reader.Length)
            {
                uint hash = reader.ReadUInt();
                int stringLen = reader.ReadInt();
                string str = reader.ReadSizedString(stringLen);
                int stringPosition = (int)reader.Position; // anthem is not really supported anyways...

                if (hashToStringIdMapping.ContainsKey(hash))
                {
                    foreach (uint stringId in hashToStringIdMapping[hash])
                        m_localizedStrings.Add(new LocalizedString(stringId, stringPosition, str));
                }
                else
                {
                    App.Logger.Log("Cannot find {0} in {1}", hash.ToString("x8"), entry.Name);
                }
            }

            m_modifiedResource = modifiedData as ModifiedPlainLocalizationResource;
            if (m_modifiedResource != null)
            {
                m_modifiedResource.InitResourceId(resRid);
            }

            // keep informed about changes...
            entry.AssetModified += (s, e) => OnModified((ResAssetEntry)s);
        }

        public override byte[] SaveBytes()
        {
            // TODO implement me!
            throw new NotImplementedException("Not yet implemented!");
        }

        public override ModifiedResource SaveModifiedResource()
        {
            return m_modifiedResource;
        }

        /// <summary>
        /// Adds or edits the text of the given id.
        /// </summary>
        /// <param name="textId"></param>
        /// <param name="text"></param>
        public void SetText(uint textId, string text)
        {

            // Try to revert if text equals original
            // -> drawback is long iteration over all texts or another huge instance of textid to text dictionary :(

            // have to try anyway as long as no dedicated remove is present..
            foreach (var entry in m_localizedStrings)
            {
                if (textId == entry.Id)
                {
                    // found the right one
                    // neither the entryValue nor the given text can be null
                    if (entry.Value.Equals(text))
                    {
                        // It is the original text, remove instead
                        RemoveText(textId);
                        return;
                    }
                    break;
                }
            }

            SetText0(textId, text);
        }

        public void RemoveText(uint textId)
        {
            if (m_modifiedResource != null)
            {
                m_modifiedResource.RemoveText(textId);

                ModifyResourceAfterDelete();
            }
        }

        public IEnumerable<uint> GetAllModifiedTextsIds()
        {
            if (m_modifiedResource == null)
            {
                return new List<uint>();
            }

            return new List<uint>(m_modifiedResource.AlteredTexts.Keys);
        }

        public IEnumerable<uint> GetDefaultTextIds()
        {
            return m_localizedStrings.Select(text => text.Id);
        }

        public IEnumerable<uint> GetAllTextIds()
        {
            return GetDefaultTextIds().Union(GetAllModifiedTextsIds());
        }

        public string GetText(uint textId)
        {
            if(m_modifiedResource != null)
            {
                bool containsText = m_modifiedResource.AlteredTexts.TryGetValue(textId, out string text);
                if(containsText)
                {
                    return text;
                }
            }

            return GetDefaultText(textId);
        }

        public string GetDefaultText(uint textId)
        {
            // if this is too slow add a dictionary for the text ids.
            foreach (var entry in m_localizedStrings)
            {
                if (textId == entry.Id)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        public bool IsStringEdited(uint id)
        {
            if(m_modifiedResource != null)
            {
                return m_modifiedResource.AlteredTexts.ContainsKey(id)
            }

            return false;
        }

        private void OnModified(ResAssetEntry assetEntry)
        {
            // There is an unhandled edge case here:
            // When a resource is completely replaced by anoher one in a mod, then this method will not pick that up!

            ModifiedAssetEntry modifiedAsset = assetEntry.ModifiedEntry;
            ModifiedPlainLocalizationResource newModifiedResource = modifiedAsset?.DataObject as ModifiedPlainLocalizationResource;

            if (newModifiedResource != m_modifiedResource)
            {
                m_modifiedResource = newModifiedResource;
                ResourceEventHandlers?.Invoke(this, new EventArgs());
            }
        }

        private void SetText0(uint textId, string text)
        {
            ModifyResourceBeforeInsert();
            m_modifiedResource.SetText(textId, text);
        }

        private void ModifyResourceBeforeInsert()
        {
            if (m_modifiedResource == null)
            {
                m_modifiedResource = new ModifiedPlainLocalizationResource();
                m_modifiedResource.InitResourceId(resRid);

                // might need to change this, when exporting the resouce it never exports the current value!
                App.AssetManager.ModifyRes(resRid, this);
            }
        }

        private void ModifyResourceAfterDelete()
        {

            if (m_modifiedResource != null
                && m_modifiedResource.AlteredTexts.Count == 0)
            {
                // remove this resource, it isn't needed anymore
                // This is also done via the listener, but whatever
                m_modifiedResource = null;

                AssetManager assetManager = App.AssetManager;
                ResAssetEntry entry = assetManager.GetResEntry(resRid);
                App.AssetManager.RevertAsset(entry);
            }
        }

    }


    /// <summary>
    /// This modified resource is used to store the altered texts only in the project file and mods.
    /// </summary>
    public class ModifiedPlainLocalizationResource : ModifiedResource
    {

        /// <summary>
        /// The dictionary of altered or new texts in this modified resource.
        /// </summary>
        public Dictionary<uint, string> AlteredTexts { get; } = new Dictionary<uint, string>();

        /// <summary>
        /// Version number that is incremented with changes to how modfiles are persisted.
        /// This should allow to detect outdated mods and maybe even read them correctly if mod writing is ever changed.
        /// Versions:
        /// 1: Includes writing the texts as number of texts + textid text tuples
        /// </summary>
        private static readonly uint m_MOD_PERSISTENCE_VERSION = 1;

        // Just to make sure we write / overwrite and merge the correct asset!
        private ulong m_resRid = 0x0;

        /// <summary>
        /// Sets a modified text into the dictionary.
        /// </summary>
        /// <param name="textId">The uint id of the string</param>
        /// <param name="text">The new string</param>
        public void SetText(uint textId, string text)
        {
            AlteredTexts[textId] = text;
        }

        /// <summary>
        /// Verbose remove method accessor.
        /// </summary>
        /// <param name="textId"></param>
        public void RemoveText(uint textId)
        {
            AlteredTexts.Remove(textId);
        }

        /// <summary>
        /// Initializes the resource id, this is used to make sure we modify and overwrite the correct resource.
        /// </summary>
        /// <param name="otherResRid"></param>
        public void InitResourceId(ulong otherResRid)
        {
            if (m_resRid != 0x0 && m_resRid != otherResRid)
            {
                string errorMsg = string.Format(
                        "Trying to initialize modified resource for resRid <{0}> with contents of resource resRid <{1}> - This may indicate a mod made for a different game or language version!",
                        m_resRid.ToString("X"), otherResRid.ToString("X"));
                App.Logger.LogWarning(errorMsg);
            }
            m_resRid = otherResRid;
        }

        /// <summary>
        /// Merges this resource with the given other resource by talking all of the other resources texts, overwriting already present texts for the same id if they exist.
        /// This method alters the state of this resource.
        /// </summary>
        /// <param name="higherPriorityModifiedResource">The other, higher priority resource, to merge into this one.</param>
        public void Merge(ModifiedPlainLocalizationResource higherPriorityModifiedResource)
        {

            if (m_resRid != higherPriorityModifiedResource.m_resRid)
            {
                string errorMsg = string.Format(
                        "Trying to merge resource with resRid <{0}> into resource for resRid <{1}> - This may indicate a mod made for a different game version!",
                        higherPriorityModifiedResource.m_resRid.ToString("X"), m_resRid.ToString("X"));
                App.Logger.LogWarning(errorMsg);
            }

            foreach (KeyValuePair<uint, string> textEntry in higherPriorityModifiedResource.AlteredTexts)
            {
                SetText(textEntry.Key, textEntry.Value);
            }
        }

        /// <summary>
        /// This function is responsible for reading in the modified data from the project file.
        /// </summary>
        /// <param name="reader"></param>
        public override void ReadInternal(NativeReader reader)
        {

            uint modPersistenceVersion = reader.ReadUInt();
            InitResourceId(reader.ReadULong());

            if (m_MOD_PERSISTENCE_VERSION < modPersistenceVersion)
            {
                ResAssetEntry asset = App.AssetManager.GetResEntry(m_resRid);
                string assetName = asset != null ? asset.Path : "<unknown>";

                string errorMessage = string.Format("A TextMod for localization resource <{0}> was written with a newer version of the Bioware Localization Plugin and cannot be read! Please update the used Plugin or remove the newer mod!", assetName);

                // TODO make this a setting?!
                bool shouldThrowExceptionOnPersistenceMisMatch = true;
                if (shouldThrowExceptionOnPersistenceMisMatch)
                {
                    throw new InvalidOperationException(errorMessage);
                }

                App.Logger.LogError(errorMessage);
                return;
            }

            ReadPrimaryVersion1Texts(reader);
        }

        /// <summary>
        /// This function is responsible for writing out the modified data to the project file.
        /// <para>I.e., the written data is this:
        /// [uint: resRid][int: numberOfEntries] {numberOfEntries * [[uint: stringId]['nullTerminatedString': String]]}
        /// </para>
        /// </summary>
        /// <param name="writer"></param>
        /// <exception cref="InvalidOperationException">If called without having initialized the resource</exception>
        public override void SaveInternal(NativeWriter writer)
        {

            // assert this is for a valid resource!
            if (m_resRid == 0x0)
            {
                throw new InvalidOperationException("Modified resource not bound to any resource!");
            }

            SaveVersion1Texts(writer);
        }

        public ulong GetResRid()
        {
            return m_resRid;
        }


        private void ReadPrimaryVersion1Texts(NativeReader reader)
        {
            int numberOfEntries = reader.ReadInt();
            for (int i = 0; i < numberOfEntries; i++)
            {
                uint textId = reader.ReadUInt();
                string text = reader.ReadNullTerminatedString();

                SetText(textId, text);
            }
        }

        private void SaveVersion1Texts(NativeWriter writer)
        {
            // version field
            writer.Write(1u);
 
            writer.Write(m_resRid);
            writer.Write(AlteredTexts.Count);

            WriteTextEntries(writer, AlteredTexts);
        }


        /// <summary>
        /// Writes the given dictionary into the given writer
        /// </summary>
        /// <param name="textEntriesToWrite"></param>
        /// <returns></returns>
        public static void WriteTextEntries(NativeWriter writer, Dictionary<uint, string> textEntriesToWrite)
        {
            // Using Frostys NativeWriter / Reader to persist texts in the mod format previously broke certain non ascii characters(even though unicode utf - 8 is used...?).
            // For that i used another writer in the original BWLocalizationPlugin implementation, which is no longer necessary it seems.
            foreach (KeyValuePair<uint, string> textEntry in textEntriesToWrite)
            {
                writer.Write(textEntry.Key);
                writer.WriteNullTerminatedString(textEntry.Value);
            }
            writer.Flush();
        }
    }
}
