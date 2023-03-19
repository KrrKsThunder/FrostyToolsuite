using Frosty.Core;
using Frosty.Hash;
using FrostyCore;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

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
            public readonly byte[] unknownStringData;

            public string Value { get; set; }


            public LocalizedString(byte[] inUnknownStringData)
            {
                unknownStringData = inUnknownStringData;
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

        // set this to true for additional debug prints.
        private static readonly bool isPrintDebugTexts = false;

        /// <summary>
        /// The default texts
        /// </summary>
        private readonly IDictionary<uint, LocalizedString> m_localizedStringsPerId = new Dictionary<uint, LocalizedString>();

        /// <summary>
        /// If any text is altered, the altered text entry will be kept in the modfiedResource.
        /// </summary>
        private ModifiedPlainLocalizationResource m_modifiedResource = null;

        private int m_headerSize = 0;

        #region header information
        private uint m_probablyLanguageIndex = 0;
        private uint m_probablyVersionNumber = 0;
        private uint m_resoureceNameHash = 0;

        private int m_numberOfUnknownSegments = 0;

        private int m_unknown1 = 0;
        private int m_unknown2 = 0;
        private int m_unknown3 = 0;

        private byte[] m_unknownSegment = new byte[0];
        #endregion

        public LocalizedStringResource()
        {
            // nothing to do.
        }

        public override void Read(NativeReader reader, AssetManager am, ResAssetEntry entry, ModifiedResource modifiedData)
        {

            base.Read(reader, am, entry, modifiedData);

            Name = entry.Filename;

            int gameProfile = ProfilesLibrary.DataVersion;

            if ((int)ProfileVersion.Anthem != gameProfile && (int)ProfileVersion.DeadSpace != gameProfile)
            {
                throw new InvalidOperationException("BwPlainStringLocalizationPlugin currenlty only supports Anthem and DeadSpace!");
            }

            // according to wannkunstbeikor, the first metadata field contains the size of the header - which is smaller than gman and i believed previously!
            byte[] metaData = entry.ResMeta;
            m_headerSize = (int)(metaData[0] | metaData[1] << 8 | metaData[2] << 16 | metaData[3] << 24);

            // actual reading starts here:
            // Please note that this read method is still very much the original anthem read method! Deadspace functionality is not guaranteed!
            // Information about the potential meaning of these fields was provided by wannkunstbeikor
            m_probablyLanguageIndex = reader.ReadUInt();
            m_probablyVersionNumber = reader.ReadUInt();
            m_resoureceNameHash = reader.ReadUInt();

            long numberOfKeys = reader.ReadLong();
            m_numberOfUnknownSegments = reader.ReadInt();
            long numberOfStrings = reader.ReadLong();

            m_unknown1 = reader.ReadInt();
            m_unknown2 = reader.ReadInt();
            m_unknown3 = reader.ReadInt();
            // position == m_headerSize bytes in the resource -> this should always be 44 bytes + 16 bytes from the metadata which are not counted

            long position = reader.Position;
            if(position != m_headerSize)
            {
                App.Logger.LogWarning("Expected reader position after reading the header of <{0}> was <{1}>, instead posisiton is <{2}>. Reading the actuald data of this resource will likely fail!", Name, m_headerSize, position);
            }

            Dictionary<uint, List<uint>> hashToStringIdMapping = ReadStringIdHashMap(reader, numberOfKeys);

            if (m_numberOfUnknownSegments > 0)
            {
                m_unknownSegment = reader.ReadBytes(m_numberOfUnknownSegments*12);
            }
            else
            {
                m_unknownSegment = new byte[0];
            }

            while (reader.Position < reader.Length)
            {
                uint hash = reader.ReadUInt();
                int stringLen = reader.ReadInt();
                string str = reader.ReadSizedString(stringLen);

                if (hashToStringIdMapping.ContainsKey(hash))
                {
                    foreach (uint stringId in hashToStringIdMapping[hash])
                    {
                        bool isTextExists = m_localizedStringsPerId.TryGetValue(stringId, out LocalizedString textEntry);
                        if (!isTextExists)
                        {
                            App.Logger.LogWarning("Text Id <{0}> was not assigned a hash value previously!");
                            textEntry = new LocalizedString(new byte[8]);
                            m_localizedStringsPerId.Add(stringId, textEntry);
                        }
                        textEntry.Value = str;
                    }
                }
                else
                {
                    App.Logger.LogWarning("Cannot find {0} in {1}", hash.ToString("x8"), entry.Name);
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

            /*
             Layout (Courtesy of Wannkunstbeikor):
                header:
                    s32 something related to languages im guessing, maybe index, 8 polish, 0 english
                    s32 seems to be 100 all the time, maybe version
                    u32 namehash of res
                    s64 number of keys
                    s32 number of unks
                    s64 number of strings
                    s32 unk[3]

                structs:
                    Key
                        s32 index
                        s32 id
                        u64 sometimes 1 only seen in translations so not english

                    Unk
                        s32 unk1
                        s64 unk2

                    String
                        s32 index
                        s32 length
                        char string[length] // utf-8 string 
             */

            // TODO rework that method to get already fixed struct of data to write
            IDictionary<uint, LocalizedString> textsEntriesToWrite = GetTextsToWrite();

            if(isPrintDebugTexts)
            {
                App.Logger.Log("Wrting text resource <{0}>, including <{1}> modified texts out of <{2}> all texts",
                    Name, GetAllModifiedTextsIds().ToList().Count, textsEntriesToWrite.Count) ;
            }

            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {

                writer.Write(m_probablyLanguageIndex);
                writer.Write(m_probablyVersionNumber);
                writer.Write(m_resoureceNameHash);

                writer.Write(textsEntriesToWrite.Count);
                writer.Write(m_numberOfUnknownSegments);
                writer.Write(textsEntriesToWrite.Count);

                writer.Write(m_unknown1);
                writer.Write(m_unknown2);
                writer.Write(m_unknown3);

                IDictionary<int, string> textsPerHash= new Dictionary<int, string>();

                foreach(var entry in textsEntriesToWrite)
                {
                    LocalizedString textEntry = entry.Value;

                    int hashValue = Fnv1a.HashString(textEntry.Value);

                    writer.Write(hashValue);
                    writer.Write(entry.Key);
                    writer.Write(textEntry.unknownStringData);

                    textsPerHash[hashValue] = textEntry.Value;
                }

                writer.Write(m_unknownSegment);

                foreach (var entry in textsPerHash)
                {
                    writer.Write(entry.Key);
                    writer.WriteSizedString(entry.Value);
                }

                return writer.ToByteArray();
            }
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
            bool isVanillaText = m_localizedStringsPerId.TryGetValue(textId, out LocalizedString textEntry);
            if(isVanillaText && textEntry.Value.Equals(text))
            {
                        // It is the original text, remove instead
                        RemoveText(textId);
                        return;
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
            return m_localizedStringsPerId.Keys;
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

            bool isDefaultText = m_localizedStringsPerId.TryGetValue(textId, out LocalizedString textEntry);
            if(isDefaultText)
            {
                return textEntry.Value;
            }

            return null;
        }

        public bool IsStringEdited(uint id)
        {
            if(m_modifiedResource != null)
            {
                return m_modifiedResource.AlteredTexts.ContainsKey(id);
            }

            return false;
        }

        /// <summary>
        /// Reads the stringid to position assignments and the unknown data for the text ids.
        /// This method fills the m_localizedStringsPerId dictionary.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="numberOfKeys"></param>
        /// <returns></returns>
        private Dictionary<uint, List<uint>> ReadStringIdHashMap(NativeReader reader, long numberOfKeys)
        {
            Dictionary<uint, List<uint>> hashToStringIdMapping = new Dictionary<uint, List<uint>>();

            for (int i = 0; i < numberOfKeys; i++)
            {
                uint supposedHashButActuallyIndex = reader.ReadUInt();
                uint stringId = reader.ReadUInt();
                byte[] unknownStringData = reader.ReadBytes(8);
                if (!hashToStringIdMapping.ContainsKey(supposedHashButActuallyIndex))
                {
                    hashToStringIdMapping.Add(supposedHashButActuallyIndex, new List<uint>());
                }
                hashToStringIdMapping[supposedHashButActuallyIndex].Add(stringId);

                m_localizedStringsPerId.Add(stringId, new LocalizedString(unknownStringData));
            }

            return hashToStringIdMapping;
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

            if(isPrintDebugTexts)
            {
                App.Logger.Log("Added or replaced text <{0}> in resource <{1}>", textId.ToString("X8"), Name);
            }
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

        /// <summary>
        /// Returns the actual texts to write back into the resource
        /// </summary>
        /// <returns></returns>
        private IDictionary<uint, LocalizedString> GetTextsToWrite()
        {

            if(m_modifiedResource == null)
            {
                return m_localizedStringsPerId;
            }

            IDictionary<uint, LocalizedString> textsToWrite = new Dictionary<uint, LocalizedString>();
            foreach ( var entry in m_localizedStringsPerId)
            {
                var locTextEntry = entry.Value;
                textsToWrite.Add(entry.Key, new LocalizedString(locTextEntry.unknownStringData)
                    {
                        Value = locTextEntry.Value
                    }
                );
            }

            foreach( var modifiedEntry in m_modifiedResource.AlteredTexts)
            {
                bool existsDefault = textsToWrite.TryGetValue(modifiedEntry.Key, out var locTextEntry);
                if(!existsDefault)
                {
                    locTextEntry = new LocalizedString(new byte[8]);
                    textsToWrite.Add(modifiedEntry.Key, locTextEntry );
                }
                locTextEntry.Value = modifiedEntry.Value;
            }

            return textsToWrite;
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
