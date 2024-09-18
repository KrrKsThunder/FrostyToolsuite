using Frosty.Core;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using System;
using System.Collections.Generic;
using System.IO;

namespace CompatibilityPatchHelperPlugin
{

    public class AssetModification
    {
        public string Name;
        public Guid AssetId;
        public EbxAssetEntry AssetEntry;
        public IList<int> AddedBundles;

        public EbxAsset Ebx { get; set; }
    }

    internal class LimitedProjectLoader
    {

        // copy from the project class, this has to be completely redone for frosty v2!
        private const uint FormatVersion = 14;
        private const ulong Magic = 0x00005954534F5246;

        /// <summary>
        /// Tries to load all the ebx modifications from the given project file and return them as dictionary of AssetModifications
        /// </summary>
        /// <param name="projectName">The name of the project file to load</param>
        /// <returns>Dictionary with asset names as keys and the AssetModification entry with all the data, including the loaded ebx as value.</returns>
        public static IDictionary<string, AssetModification> LoadFromProject(string projectName)
        {
            return LoadFromProject(projectName, false, new List<string>());
        }

        /// <summary>
        /// Tries to load the ebx modifications from the given project file if they are included in the given names to keep and return them as dictionary of AssetModifications
        /// </summary>
        /// <param name="projectName">The name of the project file to load</param>
        /// <param name="keepOnlyKnownEntries">If true, only those ebx modifications with names found in the given list are kept.</param>
        /// <param name="ebxNamesToKeep">the list of ebx names to keep. All modifications to ebx entries with other names will not be recorded or returned.</param>
        /// <returns>Dictionary with asset names as keys and the AssetModification entry with all the data, including the loaded ebx as value.</returns>
        public static IDictionary<string, AssetModification> LoadFromProject(string projectName, bool keepOnlyKnownEntries, ICollection<String> ebxNamesToKeep)
        {

            IDictionary<string, AssetModification> ebxModifications = null;
            using (NativeReader reader = new NativeReader(new FileStream(projectName, FileMode.Open, FileAccess.Read)))
            {
                ulong magic = reader.ReadULong();
                if (magic == Magic)
                {
                    ebxModifications = InternalLoad(reader, keepOnlyKnownEntries, ebxNamesToKeep, projectName);
                }
            }

            return ebxModifications;
        }

        private static IDictionary<string, AssetModification> InternalLoad(NativeReader reader, bool keepOnlyKnownEntries, ICollection<String> ebxNamesToKeep, string projectName)
        {
            // maybe i should have used Y-Wings version from their Merge Plugin as base instead of the project class...

            var ebxModifications = new Dictionary<string, AssetModification>();

            uint version = reader.ReadUInt();

            if (version != FormatVersion)
            {
                App.Logger.LogError("Abort, the given project <{0}> is of version <{1}> wich cant be read by this plugin! Only supports version 14!", projectName, version);
                return ebxModifications;
            }

            string gameProfile = reader.ReadNullTerminatedString();
            if (gameProfile.ToLower() != ProfilesLibrary.ProfileName.ToLower())
            {
                App.Logger.LogError("Selected project <{0}>  was for a different Game!", projectName);
                return ebxModifications;
            }

            // do not care about mod setup
            _ = reader.ReadLong();
            _ = reader.ReadLong();
            _ = reader.ReadUInt();

            _ = reader.ReadNullTerminatedString();
            _ = reader.ReadNullTerminatedString();
            _ = reader.ReadNullTerminatedString();
            _ = reader.ReadNullTerminatedString();
            _ = reader.ReadNullTerminatedString();

            int size = reader.ReadInt();
            if (size > 0)
            {
                _ = reader.ReadBytes(size);
            }

            for (int i = 0; i < 4; i++)
            {
                size = reader.ReadInt();
                if (size > 0)
                {
                    _ = reader.ReadBytes(size);
                }
            }

            // -----------------------------------------------------------------------------
            // added data
            // -----------------------------------------------------------------------------

            // superbundles
            int numItems = reader.ReadInt();
            // not used here

            // bundles
            numItems = reader.ReadInt();
            for (int i = 0; i < numItems; i++)
            {
                string name = reader.ReadNullTerminatedString();
                string sbName = reader.ReadNullTerminatedString();
                BundleType type = (BundleType)reader.ReadInt();

                // TODO Do i need any of this?
                //App.AssetManager.AddBundle(name, type, App.AssetManager.GetSuperBundleId(sbName));
            }

            // ebx - as ywing said, does not actually create bundles, but marks them for later update
            numItems = reader.ReadInt();
            for (int i = 0; i < numItems; i++)
            {
                string name = reader.ReadNullTerminatedString();
                Guid guid = reader.ReadGuid();

                if (!keepOnlyKnownEntries || (keepOnlyKnownEntries && ebxNamesToKeep.Contains(name)))
                {

                    EbxAssetEntry entry = new EbxAssetEntry
                    {
                        Name = name,
                        Guid = guid
                    };

                    AssetModification modification = new AssetModification()
                    {
                        Name = name,
                        AssetId = guid,
                        AssetEntry = entry
                    };

                    ebxModifications.Add(name, modification);
                }
            }

            // res
            numItems = reader.ReadInt();
            for (int i = 0; i < numItems; i++)
            {
                // I do not care about res, thos have to be merged by other means!
                _ = reader.ReadNullTerminatedString();
                _ = reader.ReadULong();
                _ = reader.ReadUInt();
                _ = reader.ReadBytes(0x10);
            }

            // chunks
            numItems = reader.ReadInt();
            // I do not care about chunks either, thos have to be merged by other means!
            // chunk read at this position in the project is a guid of 16 bytes + and int of 4 bytes, so 20 bytes offset per number;
            if (numItems > 0)
            {
                reader.ReadBytes(numItems * 20);
            }

            // -----------------------------------------------------------------------------
            // modified data
            // -----------------------------------------------------------------------------

            // ebx
            numItems = reader.ReadInt();
            for (int i = 0; i < numItems; i++)
            {
                string name = reader.ReadNullTerminatedString();
                List<AssetEntry> linkedEntries = FrostyProject.LoadLinkedAssets(reader);
                List<int> bundles = new List<int>();

                int length = reader.ReadInt();
                for (int j = 0; j < length; j++)
                {
                    string bundleName = reader.ReadNullTerminatedString();
                    int bid = App.AssetManager.GetBundleId(bundleName);
                    if (bid != -1)
                        bundles.Add(bid);
                }

                bool isModified = reader.ReadBoolean();

                bool isTransientModified = false;
                string userData = "";
                byte[] data = null;
                bool modifiedResource = false;

                if (isModified)
                {
                    isTransientModified = reader.ReadBoolean();
                    userData = reader.ReadNullTerminatedString();
                    modifiedResource = reader.ReadBoolean();
                    data = reader.ReadBytes(reader.ReadInt());
                }

                bool assetExistsInMod = ebxModifications.TryGetValue(name, out AssetModification modification);
                if (!assetExistsInMod && (!keepOnlyKnownEntries || ebxNamesToKeep.Contains(name)))
                {

                    EbxAssetEntry originalEntry = App.AssetManager.GetEbxEntry(name);

                    if (originalEntry == null)
                    {
                        App.Logger.LogWarning("Could not find asset entry <{0}>", name);
                    }
                    else
                    {
                        EbxAssetEntry copyEntry = new EbxAssetEntry()
                        {
                            Name = originalEntry.Name,
                            Guid = originalEntry.Guid
                        };

                        modification = new AssetModification()
                        {
                            Name = name,
                            AssetId = copyEntry.Guid,
                            AssetEntry = copyEntry
                        };
                        ebxModifications.Add(name, modification);
                        assetExistsInMod = true;
                    }
                }

                if (assetExistsInMod)
                {
                    EbxAssetEntry entry = modification.AssetEntry;

                    entry.LinkedAssets.AddRange(linkedEntries);
                    entry.AddedBundles.AddRange(bundles);

                    modification.AddedBundles = bundles;

                    if (isModified)
                    {
                        entry.ModifiedEntry = new ModifiedAssetEntry
                        {
                            IsTransientModified = isTransientModified,
                            UserData = userData
                        };

                        if (modifiedResource)
                        {
                            // store as modified resource data object
                            entry.ModifiedEntry.DataObject = ModifiedResource.Read(data);
                        }
                        else
                        {
                            if (!entry.IsAdded && entry.Type != null && App.PluginManager.GetCustomHandler(entry.Type) != null)
                            {
                                App.Logger.LogError("Cannot correctly read asset: {0}", name);
                            }

                            // store as a regular ebx
                            using (EbxReader ebxReader = EbxReader.CreateProjectReader(new MemoryStream(data)))
                            {
                                EbxAsset asset = ebxReader.ReadAsset<EbxAsset>();
                                entry.ModifiedEntry.DataObject = asset;

                                if (entry.IsAdded)
                                    entry.Type = asset.RootObject.GetType().Name;
                                entry.ModifiedEntry.DependentAssets.AddRange(asset.Dependencies);

                                modification.Ebx = asset;
                            }
                        }

                        // do not go into modified state on an asset that is not yet modified in the new project!?
                        //entry.OnModified();
                    }
                }
            }
            // None of the stuff that comes after this in the project is relevant to us

            return ebxModifications;
        }
    }
}
