using BwPlainStringLocalizationPlugin.LocalizedResources;
using Frosty.Core;
using FrostySdk.Managers.Entries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BwPlainStringLocalizationPlugin
{
    internal class BwPlainStringLocalizationDatabase : ILocalizedStringDatabase
    {
        /// <summary>
        /// The default language to operate with if no other one is given.
        /// </summary>
        public string DefaultLanguage { get; private set; }

        /// <summary>
        /// Holds all the languages supported by the local game and their resources.
        /// NOTE: For Anthem and Deadspace localizations there should only exist a single resource bundle per language, containing a single resource each!
        /// If this is not the case, due to e.g., dlc or similar, then a serious rewrite is necessary and this plugin might be better of as part of the main BW localization Plugin after all!
        /// </summary>
        private SortedDictionary<string, IList<LocalizedStringResource>> m_languageLocalizationResourceNames;

        /// <summary>
        /// marker whether or not this was already initialized.
        /// </summary>
        private bool m_initialized = false;

        /// <summary>
        /// Initializses the db, this is basically a plain copy of the same method in the BiowareLocalizedStringDataBase of the BiowareLocalizationPlugin
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Initialize()
        {
            DefaultLanguage = "LanguageFormat_" + Config.Get<string>("Language", "English", scope: ConfigScope.Game);

            if (m_initialized)
            {
                return;
            }

            m_languageLocalizationResourceNames = GetLanguageDictionary();

            m_initialized = true;
        }

        public void AddStringWindow()
        {
            throw new NotImplementedException();
        }

        public void BulkReplaceWindow()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<uint> EnumerateModifiedStrings()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<uint> EnumerateStrings()
        {
            throw new NotImplementedException();
        }

        public string GetString(uint id)
        {
            throw new NotImplementedException();
        }

        public string GetString(string stringId)
        {
            throw new NotImplementedException();
        }

        public bool isStringEdited(uint id)
        {
            throw new NotImplementedException();
        }

        public void RevertString(uint id)
        {
            throw new NotImplementedException();
        }

        public void SetString(uint id, string value)
        {
            throw new NotImplementedException();
        }

        public void SetString(string id, string value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Fills the language dictionary with all available languages and their resources.
        /// If this take too long it could be split into two again, first loading only the languages and their bundles, then later loading only the resources required for a selected language.
        /// </summary>
        /// <returns>Sorted Dictionary of LangugeFormat names and their text super bundles paths.</returns>
        private static SortedDictionary<string, IList<LocalizedStringResource>> GetLanguageDictionary()
        {

            var languagesRepository = new SortedDictionary<string, IList<LocalizedStringResource>>();

            // There is no need to also search for 'LocalizedStringPatchTranslationsConfiguration', these are also found via their base type
            // Or at least this is true for ME:A and DA:I, if the type hierarchy is different for Anthem or Deadspace this needs to be checked!
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx("LocalizedStringTranslationsConfiguration"))
            {
                // read localization config
                dynamic localizationAsset = App.AssetManager.GetEbx(entry).RootObject;

                // iterate through language to bundle lists
                foreach (dynamic languageBundleListEntry in localizationAsset.LanguagesToBundlesList)
                {
                    string languageName = languageBundleListEntry.Language.ToString();
                    IList<LocalizedStringResource> languageResources;
                    if (languagesRepository.ContainsKey(languageName))
                    {
                        languageResources = languagesRepository[languageName];
                    }
                    else
                    {
                        languageResources = new List<LocalizedStringResource>();
                        languagesRepository[languageName] = languageResources;
                    }

                    foreach (string bundlepath in languageBundleListEntry.BundlePaths)
                    {
                        LoadTextResource(bundlepath, languageResources);
                    }

 
                }
            }

            return languagesRepository;
        }

        /// <summary>
        /// Loads the localizedStringResources from the given budle and adds it to the given list of already loaded resources.
        /// </summary>
        /// <param name="superBundlePathPart">The name of the superbundle from which to load the resource</param>
        /// <param name="alreadyLoadedResources">The list of already loaded resources for the language, to which to add the newly loaded resource</param>
        private static void LoadTextResource(string superBundlePathPart, IList<LocalizedStringResource> alreadyLoadedResources)
        {

            ISet<ulong> existingResourceIds = alreadyLoadedResources.Select(x => x.ResourceId).ToHashSet();

            string superBundlePath = "win32/" + superBundlePathPart.ToLowerInvariant();
            foreach (ResAssetEntry resEntry in App.AssetManager.EnumerateRes(resType: (uint)ResourceType.LocalizedStringResource, bundleSubPath: superBundlePath))
            {

                // prune already loaded resources before loading them again:
                if(!existingResourceIds.Contains(resEntry.ResRid))
                {

                    LocalizedStringResource resource = App.AssetManager.GetResAs<LocalizedStringResource>(resEntry);
                    if (resource != null)
                    {
                        alreadyLoadedResources.Add(resource);
                    }
                }
            }
        }
    }
}
