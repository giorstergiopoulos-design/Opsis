using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpsisContainers
{
    /// <summary>
    /// Manages all container data operations including JSON loading, saving, and basic migrations
    /// Extracted from ContainerManager for better code organization and maintainability
    /// Handles core data persistence and simple validation/migration scenarios
    /// </summary>
    public static class ContainerDataManager
    {
        #region Private Fields - Data Storage
        // Main container data collection - moved from ContainerManager
        private static List<dynamic> _containerData;

        // JSON file path - moved from ContainerManager  
        private static string _jsonFilePath;
        #endregion

        #region Public Properties - Data Access
        /// <summary>
        /// Provides access to container data collection
        /// Used by: ContainerManager, TrayManager, CustomizeContainerForm, ItemMoveDialog
        /// Category: Data Access
        /// </summary>
        public static List<dynamic> ContainerData
        {
            get => _containerData;
            set => _containerData = value;
        }

        /// <summary>
        /// Gets the JSON file path
        /// Used by: ContainerManager, backup operations, debugging
        /// Category: File Management
        /// </summary>
        public static string JsonFilePath
        {
            get => _jsonFilePath;
            set => _jsonFilePath = value;
        }
        #endregion

        #region Initialization - Used by: ContainerManager startup
        /// <summary>
        /// Initializes the data manager with JSON file path
        /// Used by: ContainerManager during application startup
        /// Category: Initialization
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // FIX: Use ProfileManager to get the path relative to the active profile
                _jsonFilePath = ProfileManager.GetProfileFilePath("containers.json");

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                    $"ContainerDataManager initialized with path: {_jsonFilePath}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error initializing ContainerDataManager: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Core Data Operations - Used by: ContainerManager, TrayManager
        /// <summary>
        /// Main container data loading method - moved from ContainerManager.LoadContainerData
        /// Used by: ContainerManager.LoadContainerData, application startup
        /// Category: Data Loading
        /// </summary>
        public static void LoadContainerData(TargetChecker targetChecker)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    "ContainerDataManager: Starting container data load");

                // Initialize container data list
                _containerData = new List<dynamic>();

                // Check if JSON file exists
                if (!File.Exists(_jsonFilePath))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                        "JSON file not found. Starting with empty container configuration.");
                    return;
                }

                // Load and parse JSON data
                if (!LoadContainerDataFromJson())
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerCreation,
                        "Failed to load JSON data. Starting with empty configuration.");
                    return;
                }

                // Apply simple migrations and validation
                ApplySimpleMigrations();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Successfully loaded {_containerData?.Count ?? 0} containers from JSON");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Critical error in LoadContainerData: {ex.Message}");
                _containerData = new List<dynamic>(); // Fallback to empty list
            }
        }

        /// <summary>
        /// JSON file parsing and loading - moved from ContainerManager.LoadContainerDataFromJson
        /// Used by: LoadContainerData method
        /// Category: JSON Operations
        /// </summary>
        private static bool LoadContainerDataFromJson()
        {
            try
            {
                string jsonContent = LegacyNames.MigrateKeys(File.ReadAllText(_jsonFilePath));

                // Check if the file is empty or contains only whitespace
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.ContainerCreation,
                        "JSON file is empty or contains only whitespace. Using default container configuration.");
                    return false;
                }

                // First, try to parse as a list of containers
                try
                {
                    _containerData = JsonConvert.DeserializeObject<List<dynamic>>(jsonContent);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        $"Successfully loaded {_containerData?.Count ?? 0} containers from JSON array.");
                    return _containerData != null;
                }
                catch (JsonException)
                {
                    // If that fails, try to parse as a single container object
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        "Failed to parse as array, trying single object format.");

                    dynamic singleContainer = JsonConvert.DeserializeObject(jsonContent);
                    _containerData = new List<dynamic> { singleContainer };
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        "Successfully loaded single container from JSON object.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error loading container data from JSON: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves container data to JSON with consistent formatting - moved from ContainerManager.SaveContainerData
        /// Used by: ContainerManager operations, container updates, position changes
        /// Category: JSON Operations
        /// </summary>
        public static void SaveContainerData()
        {
            try
            {
                var serializedData = new List<JObject>();

                foreach (dynamic container in _containerData)
                {
                    IDictionary<string, object> containerDict = container is IDictionary<string, object> dict ?
                        dict : ((JObject)container).ToObject<IDictionary<string, object>>();

                    // Apply simple format consistency
                    ApplyFormatConsistency(containerDict);

                    serializedData.Add(JObject.FromObject(containerDict));
                }

                string formattedJson = JsonConvert.SerializeObject(serializedData, Formatting.Indented);
                File.WriteAllText(_jsonFilePath, formattedJson);
                DesktopOriginals.Reconcile(serializedData);          // Opsis: desktop icons whose item left every container come back

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.Settings,
                    $"Saved containers.json with consistent formatting for {serializedData.Count} containers");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.Settings,
                    $"Error saving container data: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Container Creation - Used by: TrayManager, ContainerManager
        /// <summary>
        /// Creates new container with proper defaults - moved from ContainerManager.CreateNewContainer
        /// Used by: TrayManager.AddContainer, portal container creation
        /// Category: Container Creation
        /// </summary>
        public static dynamic CreateNewContainer(string title, string itemsType, double x = 20, double y = 20,
            string customColor = null, string customLaunchEffect = null)
        {
            try
            {
                // Generate appropriate container name
                string containerName = (itemsType != "Portal") ?
                    CoreUtilities.GenerateRandomName() : title;

                // Create new container object
                dynamic newContainer = new System.Dynamic.ExpandoObject();
                newContainer.Id = Guid.NewGuid().ToString();
                IDictionary<string, object> newContainerDict = newContainer;

                // Set basic properties
                newContainerDict["Title"] = containerName;
                newContainerDict["X"] = x;
                newContainerDict["Y"] = y;
                newContainerDict["Width"] = 230;
                newContainerDict["Height"] = 130;
                newContainerDict["ItemsType"] = itemsType;

                // Set items based on container type
                newContainerDict["Items"] = itemsType == "Portal" ? "" : new JArray();

                // Apply default properties with simple validation
                ApplyContainerDefaults(newContainerDict, customColor, customLaunchEffect);

                // Add to data collection and save
                _containerData.Add(newContainer);
                SaveContainerData();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Created new {itemsType} container '{containerName}' with ID {newContainer.Id}");

                return newContainer;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error creating new container: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Simple Migration and Validation - Internal Use
        /// <summary>
        /// Applies simple migrations and property defaults
        /// Used by: LoadContainerData during startup
        /// Category: Data Migration (Simple)
        /// </summary>
        private static void ApplySimpleMigrations()
        {
            bool jsonModified = false;

            foreach (dynamic container in _containerData.ToList())
            {
                try
                {
                    IDictionary<string, object> containerDict = container is IDictionary<string, object> dict ?
                        dict : ((JObject)container).ToObject<IDictionary<string, object>>();

                    // Add missing basic properties
                    if (AddMissingBasicProperties(containerDict))
                        jsonModified = true;

                    // Validate and fix data types
                    if (ValidateDataTypes(containerDict))
                        jsonModified = true;
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                        $"Error applying simple migration to container: {ex.Message}");
                }
            }

            // Save if any modifications were made
            if (jsonModified)
            {
                SaveContainerData();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    "Applied simple migrations and saved updated container data");
            }
        }

        /// <summary>
        /// Adds missing basic properties with defaults
        /// Used by: ApplySimpleMigrations
        /// Category: Property Validation
        /// </summary>
        private static bool AddMissingBasicProperties(IDictionary<string, object> containerDict)
        {
            bool modified = false;

            // Add missing ID
            if (!containerDict.ContainsKey("Id") || string.IsNullOrEmpty(containerDict["Id"]?.ToString()))
            {
                containerDict["Id"] = Guid.NewGuid().ToString();
                modified = true;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                    $"Added missing ID to container '{(containerDict.ContainsKey("Title") ? containerDict["Title"] : "Unknown")}'");
            }

            // Add basic tab properties if missing
            if (!containerDict.ContainsKey("TabsEnabled"))
            {
                containerDict["TabsEnabled"] = "false";
                modified = true;
            }

            if (!containerDict.ContainsKey("CurrentTab"))
            {
                containerDict["CurrentTab"] = 0;
                modified = true;
            }

            if (!containerDict.ContainsKey("Tabs"))
            {
                containerDict["Tabs"] = new JArray();
                modified = true;
            }

            // Add basic state properties
            if (!containerDict.ContainsKey("IsHidden"))
            {
                containerDict["IsHidden"] = "false";
                modified = true;
            }

            if (!containerDict.ContainsKey("IsRolled"))
            {
                containerDict["IsRolled"] = "false";
                modified = true;
            }

            return modified;
        }

        /// <summary>
        /// Validates and fixes data types for consistency
        /// Used by: ApplySimpleMigrations
        /// Category: Data Validation
        /// </summary>
        private static bool ValidateDataTypes(IDictionary<string, object> containerDict)
        {
            bool modified = false;

            // Ensure UnrolledHeight is a valid number
            if (containerDict.ContainsKey("UnrolledHeight"))
            {
                if (!double.TryParse(containerDict["UnrolledHeight"]?.ToString(), out double unrolledHeight) || unrolledHeight <= 0)
                {
                    double defaultHeight = containerDict.ContainsKey("Height") ?
                        Convert.ToDouble(containerDict["Height"]) : 130;
                    containerDict["UnrolledHeight"] = defaultHeight.ToString();
                    modified = true;
                }
            }

            // Ensure Width and Height are valid
            if (!double.TryParse(containerDict["Width"]?.ToString(), out double width) || width <= 0)
            {
                containerDict["Width"] = 230;
                modified = true;
            }

            if (!double.TryParse(containerDict["Height"]?.ToString(), out double height) || height <= 0)
            {
                containerDict["Height"] = 130;
                modified = true;
            }

            return modified;
        }

        /// <summary>
        /// Applies format consistency for JSON serialization
        /// Used by: SaveContainerData
        /// Category: Format Consistency
        /// </summary>
        private static void ApplyFormatConsistency(IDictionary<string, object> containerDict)
        {
            // Convert IsHidden to string format
            if (containerDict.ContainsKey("IsHidden"))
            {
                bool isHidden = false;
                if (containerDict["IsHidden"] is bool boolValue)
                    isHidden = boolValue;
                else if (containerDict["IsHidden"] is string stringValue)
                    isHidden = stringValue.ToLower() == "true";

                containerDict["IsHidden"] = isHidden.ToString().ToLower();
            }

            // Convert IsRolled to string format
            if (containerDict.ContainsKey("IsRolled"))
            {
                bool isRolled = false;
                if (containerDict["IsRolled"] is bool boolValue)
                    isRolled = boolValue;
                else if (containerDict["IsRolled"] is string stringValue)
                    isRolled = stringValue.ToLower() == "true";

                containerDict["IsRolled"] = isRolled.ToString().ToLower();
            }
        }

        /// <summary>
        /// Applies default properties to new container
        /// Used by: CreateNewContainer
        /// Category: Container Defaults
        /// </summary>
        private static void ApplyContainerDefaults(IDictionary<string, object> containerDict,
             string customColor, string customLaunchEffect)
        {
            // Basic state defaults
            containerDict["IsHidden"] = "false";
            containerDict["IsRolled"] = "false";
            containerDict["UnrolledHeight"] = containerDict["Height"].ToString();

            // Tab defaults
            containerDict["TabsEnabled"] = "false";
            containerDict["CurrentTab"] = 0;
            containerDict["Tabs"] = new JArray();

            // Apply container type specific defaults
            string itemsType = containerDict["ItemsType"]?.ToString();
            if (itemsType == "Note")
            {
                NoteContainerManager.ApplyNoteDefaults(containerDict);
            }


            // Custom properties
            if (!string.IsNullOrEmpty(customColor))
                containerDict["CustomColor"] = customColor;

            if (!string.IsNullOrEmpty(customLaunchEffect))
                containerDict["CustomLaunchEffect"] = customLaunchEffect;
        }
        #endregion

        #region Data Access Helpers - Used by: Various managers
        /// <summary>
        /// Finds container by ID with null safety
        /// Used by: ContainerManager, CustomizeContainerForm, various operations
        /// Category: Data Access
        /// </summary>
        public static dynamic FindContainerById(string containerId)
        {
            if (string.IsNullOrEmpty(containerId) || _containerData == null)
                return null;

            return _containerData.FirstOrDefault(f => f.Id?.ToString() == containerId);
        }

        /// <summary>
        /// Removes container from data collection
        /// Used by: ContainerManager.DeleteContainer, cleanup operations
        /// Category: Data Modification
        /// </summary>
        public static bool RemoveContainer(dynamic container)
        {
            if (container == null || _containerData == null)
                return false;

            bool removed = _containerData.Remove(container);
            if (removed)
            {
                SaveContainerData();
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Removed container '{container.Title}' from data collection");
            }
            return removed;
        }
        #endregion
    }
}