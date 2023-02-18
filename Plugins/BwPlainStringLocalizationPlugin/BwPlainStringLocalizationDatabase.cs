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
        private SortedDictionary<string, IList<LocalizedStringResource>> m_languageLocalizationResources;

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

            m_languageLocalizationResources = GetLanguageDictionary();

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
            // might contain duplicates if there are more than one resource
            return GetResources()
                .SelectMany(resource => resource.GetAllModifiedTextsIds());
        }

        public IEnumerable<uint> EnumerateStrings()
        {
            return GetResources()
                .SelectMany(resource => resource.GetAllTextIds());
        }

        public string GetString(uint id)
        {
            return GetResources()
                .Select(resource => resource.GetText(id))
                .DefaultIfEmpty(null)
                .First();
        }

        public string GetString(string stringId)
        {
            bool canParse = uint.TryParse(stringId, out var textId);
            if(canParse)
            {
                return GetString(stringId);
            }
            App.Logger.LogWarning("Cannot read text id <{0}>", stringId);
            return null;
        }

        public bool isStringEdited(uint id)
        {
            GetResources()
                .Select(resource => resource.IsStringEdited(id))
                .DefaultIfEmpty(false)
                .First();

            return false;
        }

        public void RevertString(uint id)
        {
            GetResources().ToList().ForEach(resource => resource.RemoveText(id));
        }

        public void SetString(uint id, string value)
        {
            GetResources().ToList().ForEach(resource => resource.SetText(id, value));
        }

        public void SetString(string id, string value)
        {
            bool canParse = uint.TryParse(id, out var textId);
            if (canParse)
            {
                SetString(textId, value);
            }
            else
            {
                App.Logger.LogWarning("Cannot read text id <{0}>", id);
            }
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

        /// <summary>
        /// Returns the resources for the default language.
        /// </summary>
        /// <returns></returns>
        private IList<LocalizedStringResource> GetResources()
        {
            return GetResources(DefaultLanguage);
        }

        /// <summary>
        /// Returns the resources for the given language
        /// </summary>
        /// <param name="languageFormat"></param>
        /// <returns></returns>
        private IList<LocalizedStringResource> GetResources(string languageFormat)
        {
            bool containsLanguage = m_languageLocalizationResources.TryGetValue(languageFormat, out var resources);
            if(containsLanguage)
            {

                if(resources.Count>1)
                {
                    App.Logger.LogWarning("Language <{0}> contains more than one resource! Results will not be accurate!", languageFormat);
                }

                return resources;
            }

            App.Logger.LogError("Language <{0}> is not found in the game!", languageFormat);
            return new List<LocalizedStringResource>();
        }
    }
}
