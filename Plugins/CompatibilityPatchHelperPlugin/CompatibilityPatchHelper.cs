using Frosty.Core;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CompatibilityPatchHelperPlugin
{

    public class CompatibilityPatchHelper
    {

        public static void MergeAssets(AssetModification modification1, AssetModification modification2)
        {
            string assetName = modification1.Name;

            EbxAsset mergedAsset = MergeAssets(assetName, modification1.Ebx, modification2.Ebx);

            var addedBundleSet = modification1.AddedBundles.ToHashSet();
            foreach (var addedBundle in modification2.AddedBundles)
            {
                addedBundleSet.Add(addedBundle);
            }

            EbxAssetEntry mergedAssetEntry = App.AssetManager.AddEbx(assetName, mergedAsset, addedBundleSet.ToArray());

            // have to call these methods again for vanilla assets or same assets
            mergedAssetEntry.AddToBundles(addedBundleSet);
            App.AssetManager.ModifyEbx(assetName, mergedAsset);
        }

        /// <summary>
        /// Tries to create a merged version of the both given EbxAssets. Can only perform additive merges.
        /// </summary>
        /// <param name="assetName"></param>
        /// <param name="asset1"></param>
        /// <param name="asset2"></param>
        /// <returns>A new asset containing all entries of both given ones</returns>
        internal static EbxAsset MergeAssets(string assetName, EbxAsset asset1, EbxAsset asset2)
        {
            // for now just assume they are the same and continue to merging

            // stolen from the duplication plugin for how to best recreate an asset:
            EbxAsset mergedAsset;
            using (EbxBaseWriter writer = EbxBaseWriter.CreateWriter(new MemoryStream(), EbxWriteFlags.DoNotSort))
            {
                writer.WriteAsset(asset1);
                byte[] buf = writer.ToByteArray();
                using (EbxReader reader = EbxReader.CreateReader(new MemoryStream(buf)))
                    mergedAsset = reader.ReadAsset<EbxAsset>();
            }

            Dictionary<AssetClassGuid, object> mergedObjectsMap = mergedAsset.Objects.ToDictionary(entry => (AssetClassGuid)((dynamic)entry).GetInstanceGuid());

            foreach (dynamic asset2Object in asset2.Objects)
            {
                AssetClassGuid asset2Guid = asset2Object.GetInstanceGuid();

                if (mergedObjectsMap.TryGetValue(asset2Guid, out object mergedObject))
                {
                    MergeObject(assetName, mergedObject, asset2Object);
                }
                else
                {
                    // should this object be duplicated first?
                    mergedAsset.AddObject(asset2Object);
                    mergedObjectsMap[asset2Guid] = asset2Object;
                }
            }

            return mergedAsset;
        }

        // or is this only for root objects?!
        private static bool MergeObject(string assetName, object o1, object o2)
        {
            Type type = o1.GetType();
            if (type != o2.GetType())
            {
                throw new ArgumentException("Types to merge don't match!");
            }

            switch (type.Name)
            {
                case "MasterItemList":
                    MergeMasterItemList(o1, o2);
                    break;
                case "ItemList":
                    MergeItemList(o1, o2);
                    break;
                case "NetworkRegistryAsset":
                    MergeNetworkRegistry(o1, o2);
                    break;
                case "MeshVariationDatabase":
                    MergeMeshVariationDatabase(o1, o2);
                    break;
                default:
                    // no idea, maybe a printout?
                    App.Logger.LogWarning("Cannot merge ebx asset <{0}> of type {1}! You have to manually do that!", assetName, type.Name);
                    return false;
            }

            return true;
        }

        private static void MergeMasterItemList(dynamic mergedMasterItemList, dynamic otherMasterItemList)
        {
            List<PointerRef> mergedItemRefs = mergedMasterItemList.ItemAssets;
            List<PointerRef> otherItemRefs = otherMasterItemList.ItemAssets;

            MergeListOfPointerRefs(mergedItemRefs, otherItemRefs);
        }

        private static void MergeItemList(dynamic mergeItemList, dynamic otherItemList)
        {
            /*
             * TODO Discern differences between MEA and DAI?
             * DAI ItemList == MEA MasterItemList => List<PointerRef> ItemAssets;
             * while MEA ItemList has 'items': List<ItemQuantityInfo> Items;
             */

            if (ProfilesLibrary.DataVersion != (int)ProfileVersion.DragonAgeInquisition)
            {
                throw new ArgumentException("Cannot merge itemList for selected game!");
            }

            List<PointerRef> mergedItemRefs = mergeItemList.ItemAssets;
            List<PointerRef> otherItemRefs = otherItemList.ItemAssets;

            MergeListOfPointerRefs(mergedItemRefs, otherItemRefs);
        }

        private static void MergeNetworkRegistry(dynamic mergedNetworkRegistry, dynamic otherNetworkRegistry)
        {
            List<PointerRef> mergedContainerRefs = mergedNetworkRegistry.Objects;
            List<PointerRef> otherContainerRefs = otherNetworkRegistry.Objects;

            MergeListOfPointerRefs(mergedContainerRefs, otherContainerRefs);
        }

        private static void MergeListOfPointerRefs(List<PointerRef> mergeList, List<PointerRef> otherList)
        {
            //PointerRefs.External has equals and hashcode!
            // create dict of all in first, iterate over second, and add missing entries to first!

            var pointerMap = mergeList.ToDictionary(pointerRef => pointerRef.External);

            foreach (PointerRef pointerRef in otherList)
            {
                var externalPointer = pointerRef.External;
                if (!pointerMap.ContainsKey(externalPointer))
                {
                    mergeList.Add(pointerRef);
                    pointerMap[externalPointer] = pointerRef;
                }
            }
        }

        private static void MergeMeshVariationDatabase(dynamic mergedMVDB, dynamic otherMVDB)
        {
            // i don't think using the MeshVariationDatabase Class from frosty core would be correct here, that seems to be for frosty preview only?

            // lists can be null, so if the other is filled we have to return that one instead
            mergedMVDB.Entries = MergeMeshVariationDatabaseEntries(mergedMVDB.Entries, otherMVDB.Entries);
            mergedMVDB.RedirectEntries = MergeMeshVariationDatabaseEntries(mergedMVDB.RedirectEntries, otherMVDB.RedirectEntries);
        }

        // key entry used to easier compare MVDB Entries or ReferenceEntries using a dictionary
        private class MVDBEntryKey
        {
            readonly PointerRef MeshReference;
            readonly UInt32 VariationAssetNameHash;

            public MVDBEntryKey(PointerRef InMeshReference, UInt32 InVariationAssetNameHash)
            {
                MeshReference = InMeshReference;
                VariationAssetNameHash = InVariationAssetNameHash;
            }

            public static bool operator ==(MVDBEntryKey A, object B) => A.Equals(B);

            public static bool operator !=(MVDBEntryKey A, object B) => !A.Equals(B);

            // override object.Equals
            public override bool Equals(object obj)
            {

                if (obj is MVDBEntryKey otherMvdbEntryKey)
                {
                    if (MeshReference.Equals(otherMvdbEntryKey.MeshReference)
                        && VariationAssetNameHash == otherMvdbEntryKey.VariationAssetNameHash)
                    {
                        return true;
                    }
                }
                return false;
            }

            // override object.GetHashCode
            public override int GetHashCode()
            {
                int hash = 11;
                hash ^= MeshReference.GetHashCode();
                hash ^= VariationAssetNameHash.GetHashCode();

                return hash;
            }

        }

        // should work for MeshVariationDatabaseEntry and MeshVariationDatabaseRedirectEntry
        //private static void MergeMeshVariationDatabaseEntries(List<dynamic> mergedEntries, List<dynamic> otherEntries)
        private static IList MergeMeshVariationDatabaseEntries(object InNullableList, object InNullableOtherList)
        {

            if (InNullableList == null)
            {
                return (IList)InNullableOtherList;
            }

            if ((InNullableList is IList mergedEntries)
                && (InNullableOtherList is IList otherEntries))
            {

                var existingEntries = new Dictionary<MVDBEntryKey, dynamic>();
                foreach (dynamic existingEntry in mergedEntries)
                {
                    var key = new MVDBEntryKey(existingEntry.Mesh, existingEntry.VariationAssetNameHash);
                    existingEntries[key] = existingEntry;
                }

                foreach (dynamic otherEntry in otherEntries)
                {
                    MVDBEntryKey otherKey = new MVDBEntryKey(otherEntry.Mesh, otherEntry.VariationAssetNameHash);
                    if (!existingEntries.ContainsKey(otherKey))
                    {
                        mergedEntries.Add(otherEntry);
                        existingEntries[otherKey] = otherEntry;
                    }
                }

                return mergedEntries;
            }

            // else something weird is going on...
            App.Logger.LogWarning("Can't merge MeshVariationDatabaseEntry!");
            return (IList)InNullableList;
        }
    }
}
