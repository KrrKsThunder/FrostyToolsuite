using Frosty.Core;
using Frosty.Hash;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using FrostySdk.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BwPlainStringLocalizationPlugin.LocalizedResources
{

    /// <summary>
    /// Implementation of the LocalizedStringResource - Resource type.
    /// Please note that there is another implementation with the same name for those resources used in MEA and DAI in BiowareLocalizationPlugin.LocalizedResources!
    /// </summary>
    public class LocalizedStringResource : Resource
    {

        #region key
        public class TextKey : IComparable<TextKey>
        {
            public readonly uint id;
            public readonly long variation;

            public TextKey(uint inId, long inVariation)
            {
                id = inId;
                variation = inVariation;
            }

            public override int GetHashCode()
            {
                return 31 * id.GetHashCode() + variation.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj == null || obj.GetType() != typeof(TextKey))
                {
                    return false;
                }

                TextKey otherKey = (TextKey)obj;
                return id == otherKey.id
                    && variation == otherKey.variation;
            }

            public int CompareTo(TextKey other)
            {
                int value = id.CompareTo(other.id);
                if(value != 0)
                {
                    return value;
                }
                return variation.CompareTo(other.variation);
            }
        }
        #endregion

        #region string definition class
        internal class LocalizedString
        {
            public readonly long genderedTextVariant;

            public string Text { get; set; }


            public LocalizedString(long inGenderedTextVariant)
            {
                genderedTextVariant = inGenderedTextVariant;
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
        private readonly IDictionary<uint, IList<LocalizedString>> m_localizedStringsPerId = new Dictionary<uint, IList<LocalizedString>>();

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

            // according to wannkunstbeikor, the first metadata field contains the size of the header.
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
            if (position != m_headerSize)
            {
                App.Logger.LogWarning("Expected reader position after reading the header of <{0}> was <{1}>, instead posisiton is <{2}>. Reading the actuald data of this resource will likely fail!", Name, m_headerSize, position);
            }

            var indexToStringIdMapping = ReadStringIdHashMap(reader, numberOfKeys);

            if (m_numberOfUnknownSegments > 0)
            {
                m_unknownSegment = reader.ReadBytes(m_numberOfUnknownSegments * 12);
            }
            else
            {
                m_unknownSegment = new byte[0];
            }

            while (reader.Position < reader.Length)
            {
                uint index = reader.ReadUInt();
                int stringLen = reader.ReadInt();
                string str = Encoding.UTF8.GetString(reader.ReadBytes(stringLen));

                if (indexToStringIdMapping.ContainsKey(index))
                {
                    foreach (var indexEntry in indexToStringIdMapping[index])
                    {

                        uint stringId = indexEntry.Key;
                        long variation = indexEntry.Value;

                        LocalizedString textEntry;
                        bool isTextExists = m_localizedStringsPerId.TryGetValue(stringId, out IList<LocalizedString> textEntries);
                        if (!isTextExists)
                        {
                            App.Logger.LogWarning("Text Id <{0}> was not assigned to an index or variation previously!");
                            textEntries = new List<LocalizedString>();
                            m_localizedStringsPerId.Add(stringId, textEntries);
                        }
                        textEntry = new LocalizedString(variation) { Text = str };
                        textEntries.Add(textEntry);
                        // TODO make sure that variations exist only once!
                    }
                }
                else
                {
                    App.Logger.LogWarning("Cannot find {0} in {1}", index.ToString("x8"), entry.Name);
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
                        u64 sometimes 1 only seen in translations so not english - Seems to be for gendered text variations.

                    Unk
                        s32 unk1
                        s64 unk2

                    String
                        s32 index
                        s32 length
                        char string[length] // utf-8 string 
             */

            IDictionary<uint, IList<LocalizedString>> textsEntriesToWriteById = GetTextsToWrite();
            var textEntryListsByIndex = GetTextsToWriteByIndex(textsEntriesToWriteById);

            int numberOfTextKeys = textEntryListsByIndex.Count;
            int numberOfUniqueStrings = textEntryListsByIndex.Sum(entry => entry.Value.Count);

            if (isPrintDebugTexts)
            {
                App.Logger.Log("Wrting text resource <{0}>, including <{1}> modified texts out of <{2}> all texts, mapped into <{3}> distinct text indexes.",
                    Name, GetAllModifiedTextsIds().ToList().Count, numberOfTextKeys, numberOfUniqueStrings);
            }

            using (NativeWriter writer = new NativeWriter(new MemoryStream()))
            {

                writer.Write(m_probablyLanguageIndex);
                writer.Write(m_probablyVersionNumber);
                writer.Write(m_resoureceNameHash);

                writer.Write(numberOfTextKeys);
                writer.Write(m_numberOfUnknownSegments);
                writer.Write(numberOfUniqueStrings);

                writer.Write(m_unknown1);
                writer.Write(m_unknown2);
                writer.Write(m_unknown3);

                IDictionary<int, string> textsPerHash = new Dictionary<int, string>();

                foreach (var indexMapping in textEntryListsByIndex)
                {
                    int index = indexMapping.Key;

                    foreach (var textToIdMapping in indexMapping.Value)
                    {
                        writer.Write(index);
                        writer.Write(textToIdMapping.Key);
                        writer.Write(textToIdMapping.Value.genderedTextVariant);
                    }
                }

                writer.Write(m_unknownSegment);

                foreach (var indexMapping in textEntryListsByIndex)
                {
                    int index = indexMapping.Key;
                    LocalizedString firstEntry = indexMapping.Value[0].Value;
                    string text = firstEntry.Text;

                    byte[] stringAsBytes = Encoding.UTF8.GetBytes(text);

                    writer.Write(index);
                    writer.Write(stringAsBytes.Length);
                    writer.Write(stringAsBytes);
                }

                // header size should always remain the same, so there is no need to adapt the metadata here.
                return writer.ToByteArray();
            }
        }

        public override ModifiedResource SaveModifiedResource()
        {
            return m_modifiedResource;
        }

        /// <summary>
        /// Sets the given text for the given id and variations
        /// </summary>
        /// <param name="textId"></param>
        /// <param name="text"></param>
        /// <param name="variation"></param>
        public void SetText(uint textId, string text, long variation)
        {

            // Try to revert if text equals original
            bool isVanillaText = m_localizedStringsPerId.TryGetValue(textId, out IList<LocalizedString> textEntries);

            var textEntry = textEntries.Where(listEntry => listEntry.genderedTextVariant == variation).FirstOrDefault();

            if (isVanillaText && textEntry != null && textEntry.Text.Equals(text))
            {
                // It is the original text, remove instead
                RemoveText(textId, variation);
                return;
            }
            SetText0(textId, text, variation);
        }

        public void RemoveText(uint textId, long variation)
        {
            if (m_modifiedResource != null)
            {
                m_modifiedResource.RemoveText(textId, variation);

                ModifyResourceAfterDelete();
            }
        }

        public IEnumerable<uint> GetAllModifiedTextsIds()
        {
            if (m_modifiedResource == null)
            {
                return new List<uint>();
            }

            return m_modifiedResource.AlteredTexts
                .Keys
                    .Select(key => key.id)
                    .Distinct();
        }

        public IEnumerable<uint> GetDefaultTextIds()
        {
            return m_localizedStringsPerId.Keys;
        }

        public IEnumerable<uint> GetAllTextIds()
        {
            return GetDefaultTextIds().Union(GetAllModifiedTextsIds());
        }

        /// <summary>
        /// Returns the List of variation numbers for the text of the given Id.
        /// </summary>
        /// <param name="textId">The text id for which to look for variations</param>
        /// <returns>The existing variations for the text id</returns>
        public IEnumerable<long> GetTextVariationNumbers(uint textId)
        {
            List<long> variationsIdList = new List<long>();
            if (m_modifiedResource != null)
            {
                variationsIdList.AddRange(m_modifiedResource.AlteredTexts.Where(entry => entry.Key.id == textId).Select(entry => entry.Key.variation));
            }

            bool entryExists = m_localizedStringsPerId.TryGetValue(textId, out var textVariationsList);
            if(entryExists)
            {
                variationsIdList.Union(textVariationsList.Select(entry=> entry.genderedTextVariant));
            }

            return variationsIdList;
        }


        public string GetText(uint textId, long variation)
        {
            if (m_modifiedResource != null)
            {
                TextKey key = new TextKey(textId, variation);
                bool containsText = m_modifiedResource.AlteredTexts.TryGetValue(key, out string text);
                if (containsText)
                {
                    return text;
                }
            }

            return GetDefaultText(textId, variation);
        }

        public string GetDefaultText(uint textId, long variation)
        {

            bool textIdExists = m_localizedStringsPerId.TryGetValue(textId, out IList<LocalizedString> textEntries);

            if (!textIdExists)
            {

                return null;
            }

            return textEntries
                .Where(textEntry => textEntry.genderedTextVariant == variation)
                .Select(textEntry => textEntry.Text)
                .FirstOrDefault();
        }

        public bool IsStringEdited(uint id, long variation)
        {
            if (m_modifiedResource != null)
            {
                return m_modifiedResource.AlteredTexts.ContainsKey(new TextKey(id, variation));
            }

            return false;
        }

        /// <summary>
        /// Reads the stringid to position assignments and the unknown data for the text ids.
        /// This method fills the m_localizedStringsPerId dictionary.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="numberOfKeys"></param>
        /// <returns>an index, and mapped to that a list of key valuepairs for id and variant</returns>
        private Dictionary<uint, IList<KeyValuePair<uint, long>>> ReadStringIdHashMap(NativeReader reader, long numberOfKeys)
        {
            Dictionary<uint, IList<KeyValuePair<uint, long>>> hashToStringIdMapping = new Dictionary<uint, IList<KeyValuePair<uint, long>>>();

            for (int i = 0; i < numberOfKeys; i++)
            {
                uint supposedHashButActuallyIndex = reader.ReadUInt();
                uint stringId = reader.ReadUInt();
                long textVariant = reader.ReadLong();
                if (!hashToStringIdMapping.ContainsKey(supposedHashButActuallyIndex))
                {
                    hashToStringIdMapping.Add(supposedHashButActuallyIndex, new List<KeyValuePair<uint, long>>());
                }
                hashToStringIdMapping[supposedHashButActuallyIndex].Add(new KeyValuePair<uint, long>(stringId, textVariant));

                if (!m_localizedStringsPerId.ContainsKey(stringId))
                {
                    m_localizedStringsPerId.Add(stringId, new List<LocalizedString>());
                }
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

        private void SetText0(uint textId, string text, long variation)
        {
            ModifyResourceBeforeInsert();
            m_modifiedResource.SetText(textId, text, variation);

            if (isPrintDebugTexts)
            {
                App.Logger.Log("Added or replaced text <{0}> variation <{1}> in resource <{2}>", textId.ToString("X8"), variation, Name);
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
        /// Returns the actual texts to write back into the resource. Mapped to their id value is the list of all texts with the same id, but different variations
        /// </summary>
        /// <returns></returns>
        private IDictionary<uint, IList<LocalizedString>> GetTextsToWrite()
        {

            if (m_modifiedResource == null)
            {
                return m_localizedStringsPerId;
            }

            IDictionary<uint, IList<LocalizedString>> textsToWrite = new SortedDictionary<uint, IList<LocalizedString>>();
            foreach (var entry in m_localizedStringsPerId)
            {
                var locTextEntryList = entry.Value;
                IList<LocalizedString> idTextList = new List<LocalizedString>();
                textsToWrite.Add(entry.Key, idTextList);

                foreach (LocalizedString textEntry in locTextEntryList)
                {
                    idTextList.Add(new LocalizedString(textEntry.genderedTextVariant)
                    {
                        Text = textEntry.Text
                    }
                    );
                }
            }

            foreach (var modifiedEntry in m_modifiedResource.AlteredTexts)
            {
                bool existsDefault = textsToWrite.TryGetValue(modifiedEntry.Key.id, out var locTextEntryList);
                if (!existsDefault)
                {

                    locTextEntryList = new List<LocalizedString>();
                    textsToWrite.Add(modifiedEntry.Key.id, locTextEntryList);
                }

                if (!UpdateTextEntryToWrite(locTextEntryList, modifiedEntry.Key.variation, modifiedEntry.Value))
                {
                    LocalizedString locTextEntry = new LocalizedString(modifiedEntry.Key.variation) { Text = modifiedEntry.Value };
                    locTextEntryList.Add(locTextEntry);
                }
            }

            return textsToWrite;
        }

        private bool UpdateTextEntryToWrite(IList<LocalizedString> listToUpdate, long variation, string text)
        {

            foreach (var entry in listToUpdate)
            {
                if (entry.genderedTextVariant == variation)
                {
                    entry.Text = text;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a dictionary of lists of texts and their Id mapping, mapped to an index. Same texts receive are found mapped to the same index.
        /// --
        /// Turns out the idea of the id mapping is to have the same id in different variations for different genders instead...
        /// </summary>
        /// <param name="textsToWriteById">The result of 'GetTextsToWrite()'</param>
        /// <returns>(Sorted) Dictionary of indices and all the text values to write at that index.</returns>
        private IDictionary<int, IList<KeyValuePair<uint, LocalizedString>>> GetTextsToWriteByIndex(IDictionary<uint, IList<LocalizedString>> textsToWriteById)
        {

            var textEntriesMappedToIndex = new SortedDictionary<int, IList<KeyValuePair<uint, LocalizedString>>>();
            var textIndexByHashValue = new Dictionary<int, int>();

            int index = 0;
            foreach (var entry in textsToWriteById)
            {
                IList<LocalizedString> textList = entry.Value;
                IList<LocalizedString> sortedList = textList.OrderBy(text => text.genderedTextVariant).ToList();

                foreach (LocalizedString textEntry in sortedList)
                {
                    string text = textEntry.Text;
                    int hashValue = Fnv1a.HashString(textEntry.Text);

                    bool textAlreadyExists = textIndexByHashValue.TryGetValue(hashValue, out int existingIndex);
                    if (textAlreadyExists)
                    {
                        var indexList = textEntriesMappedToIndex[existingIndex];
                        indexList.Add(new KeyValuePair<uint, LocalizedString>(entry.Key, textEntry));
                    }
                    else
                    {
                        var indexList = new List<KeyValuePair<uint, LocalizedString>>
                        {
                            new KeyValuePair<uint, LocalizedString>(entry.Key, textEntry)
                        };

                        textEntriesMappedToIndex[index] = indexList;
                        textIndexByHashValue[hashValue] = index;
                        index++;
                    }
                }
            }

            return textEntriesMappedToIndex;
        }


        /// <summary>
        /// This modified resource is used to store the altered texts only in the project file and mods.
        /// </summary>
        public class ModifiedPlainLocalizationResource : ModifiedResource
        {

            /// <summary>
            /// The dictionary of altered or new texts in this modified resource.
            /// </summary>
            public Dictionary<TextKey, string> AlteredTexts { get; } = new Dictionary<TextKey, string>();

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
            /// <param name="variation">The string variation</param>
            public void SetText(uint textId, string text, long variation)
            {
                AlteredTexts[new TextKey(textId, variation)] = text;
            }

            /// <summary>
            /// Verbose remove method accessor.
            /// </summary>
            /// <param name="textId"></param>
            /// <param name="variation">The string variation</param>
            public void RemoveText(uint textId, long variation)
            {
                AlteredTexts.Remove(new TextKey(textId, variation));
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

                foreach (KeyValuePair<TextKey, string> textEntry in higherPriorityModifiedResource.AlteredTexts)
                {
                    AlteredTexts[textEntry.Key] = textEntry.Value;
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
                    long variation = reader.ReadLong();
                    string text = reader.ReadNullTerminatedString();

                    SetText(textId, text, variation);
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
            public static void WriteTextEntries(NativeWriter writer, Dictionary<TextKey, string> textEntriesToWrite)
            {
                // Using Frostys NativeWriter / Reader to persist texts in the mod format previously broke certain non ascii characters(even though unicode utf - 8 is used...?).
                // For that i used another writer in the original BWLocalizationPlugin implementation, which is no longer necessary it seems.
                foreach (KeyValuePair<TextKey, string> textEntry in textEntriesToWrite)
                {
                    TextKey key = textEntry.Key;
                    writer.Write(key.id);
                    writer.Write(key.variation);
                    writer.WriteNullTerminatedString(textEntry.Value);
                }
                writer.Flush();
            }
        }
    }
}
