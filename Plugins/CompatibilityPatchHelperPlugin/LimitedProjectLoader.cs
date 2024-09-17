using Frosty.Core;
using Frosty.Core.Controls;
using Frosty.Core.Handlers;
using Frosty.Core.Mod;
using Frosty.Hash;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompatibilityPatchHelperPlugin
{
    internal class LimitedProjectLoader
    {
        private struct AssetModification
        {
            public string Name;
            public Guid AssetId;
            public EbxAssetEntry AssetEntry;
            public IList<int> AddedBundles;
            public EbxAsset Ebx;
        }

        // copy from the project class, this has to be completely redone for v2!
        private const uint FormatVersion = 14;
        private const ulong Magic = 0x00005954534F5246;

        public void LoadFromProject(string projectName)
        {

            using (NativeReader reader = new NativeReader(new FileStream(projectName, FileMode.Open, FileAccess.Read)))
            {
                ulong magic = reader.ReadULong();
                if (magic == Magic)
                {
                    // TODO setup and memory still missing!
                    InternalLoad(reader);
                }
            }
        }

        private bool InternalLoad(NativeReader reader)
        {
            // maybe i should have used Y-Wings version from their Merge Plugin as base instead of the project class...

            // TODO move dictionary to outside or something
            var ebxModifications = new Dictionary<string, AssetModification>();

            uint version = reader.ReadUInt();

            if (version != FormatVersion)
            {
                App.Logger.LogError("Abort, the given project is of version <{0}> wich cant be read by this plugin! Only supports version 14!", version);
                return false;
            }

            string gameProfile = reader.ReadNullTerminatedString();
            if (gameProfile.ToLower() != ProfilesLibrary.ProfileName.ToLower())
            {
                App.Logger.LogError("Selected project was for a different Game!");
                return false;
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
                EbxAssetEntry entry = new EbxAssetEntry
                {
                    Name = name,
                    Guid = reader.ReadGuid()
                };

                AssetModification modification = new AssetModification()
                {
                    Name = name,
                    AssetId = entry.Guid,
                    AssetEntry = entry
                };

                ebxModifications.Add(name, modification);
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
                if(assetExistsInMod)
                {
                    EbxAssetEntry entry = modification.AssetEntry;

                    // FIXME check for duplicates here!
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
                            if (!entry.IsAdded && App.PluginManager.GetCustomHandler(entry.Type) != null)
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

            // None of the stuff that comes after this is relevant to us

            return true;
        }
    }
}
