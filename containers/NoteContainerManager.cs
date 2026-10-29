using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace OpsisContainers
{
    /// <summary>
    /// Manages Note Container specific functionality including content management, context menus, and formatting
    /// Dedicated class for Note Container operations to maintain separation from Data/Portal container logic
    /// Used by: ContainerManager.CreateContainer() for Note type containers
    /// Category: Note Container Management
    /// </summary>
    public static class NoteContainerManager
    {
        #region Note Content Creation - Used by: ContainerManager.CreateContainer()

        /// <summary>
        /// Creates the note content area (TextBox) for Note containers
        /// Used by: ContainerManager.CreateContainer() when ItemsType == "Note"
        /// Category: UI Creation
        /// </summary>
        /// <param name="container">The container data object</param>
        /// <param name="dockPanel">The parent DockPanel to add content to</param>
        /// <returns>The created TextBox for note content</returns>
        public static TextBox CreateNoteContent(dynamic container, DockPanel dockPanel)
        {
            try
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                    $"Creating note content for container '{container.Title}'");

                // Safely get note content with null checks
                string noteContent = "";
                try
                {
                    noteContent = container.NoteContent?.ToString() ?? "";
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // NoteContent property doesn't exist yet - use empty string
                    noteContent = "";
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        "NoteContent property not found - using empty content for new Note container");
                }

                // Safely get other Note properties with fallbacks
                string wordWrap = GetSafeNoteProperty(container, "WordWrap", "true");
                string spellCheck = GetSafeNoteProperty(container, "SpellCheck", "true");
                string noteFontSize = GetSafeNoteProperty(container, "NoteFontSize", "Medium");
                string noteFontFamily = GetSafeNoteProperty(container, "NoteFontFamily", "Segoe UI");

                // Debug logging to see what values we're getting
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Loading Note container settings - FontSize: '{noteFontSize}', FontFamily: '{noteFontFamily}'");
               

                                // Create the main TextBox for note content
                                TextBox noteTextBox = new TextBox
                {
                    // Content and behavior
                    Text = noteContent,
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    TextWrapping = GetTextWrapping(wordWrap),
                    SpellCheck = { IsEnabled = GetSpellCheck(spellCheck) },

                                    // Appearance - Start transparent, will be updated by ApplyNoteColorScheme
                                    //  Background = Brushes.Transparent,
                                    Background = GetTextBackgroundBrush(container),
                                    Foreground = GetNoteForeground(container),
                                 
                                    FontSize = GetNoteFontSize(noteFontSize),
                    FontFamily = GetNoteFontFamily(noteFontFamily),
                    BorderThickness = GetTextBoxBorder().thickness,
                    BorderBrush = GetTextBoxBorder().brush,

                                    //// Layout
                                    //HorizontalAlignment = HorizontalAlignment.Stretch,
                                    //VerticalAlignment = VerticalAlignment.Stretch,
                                    //Margin = new Thickness(18),
                                    //Padding = new Thickness(4, 6, 8, 6),
                                    // Layout - TWEAKABLE VALUES
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = GetTextBoxMargin(), // Custom spacing from container edges
                    Padding = GetTextBoxPadding(), // Text spacing inside TextBox

                                    // Scrolling
                    VerticalScrollBarVisibility = SettingsManager.DisableContainerScrollbars ?
                        ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,

                    // Cursor
                    Cursor = Cursors.IBeam,

//                    // Add drop shadow effect for better text visibility
//Effect = new DropShadowEffect
//{
//    Color = Colors.Black,
//    Direction = 315,
//    ShadowDepth = 2,
//    BlurRadius = 3,
//    Opacity = 0.8
//}

                                };

                // Apply exact container background color
                ApplyNoteColorScheme(noteTextBox, container);

                // Add visual state management and auto-save functionality
                SetupNoteEditingBehavior(noteTextBox, container);

                // Add to DockPanel (fills remaining space after title)
                dockPanel.Children.Add(noteTextBox);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.ContainerCreation,
                    $"Successfully created note content for container '{container.Title}' with {noteContent.Length} characters");

                return noteTextBox;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error creating note content: {ex.Message}");

                // Return a basic TextBox as fallback - but make it functional
                var fallbackTextBox = new TextBox
                {
                    Text = "",  // Empty instead of error message
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    Background = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(2),
                    Padding = new Thickness(8, 6, 8, 6)
                };

                // Add to DockPanel
                dockPanel.Children.Add(fallbackTextBox);

                // Even for fallback, add basic functionality
                SetupNoteEditingBehavior(fallbackTextBox, container);

                return fallbackTextBox;
            }
        }

        /// <summary>
        /// Safely gets Note container properties with fallback values
        /// </summary>
        /// <summary>
        /// Safely gets Note container properties with fallback values
        /// </summary>
        public static string GetSafeNoteProperty(dynamic container, string propertyName, string fallbackValue)
        {
            try
            {
                // Method 1: Direct property access
                try
                {
                    var value = container.GetType().GetProperty(propertyName)?.GetValue(container);
                    if (value != null) return value.ToString();
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }

                // Method 2: Dictionary access  
                try
                {
                    var containerDict = container as IDictionary<string, object>;
                    if (containerDict != null && containerDict.ContainsKey(propertyName))
                    {
                        var value = containerDict[propertyName]?.ToString();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                            $"GetSafeNoteProperty: Found {propertyName} = '{value}' in dictionary");
                        return value ?? fallbackValue;
                    }
                }
                catch { }

                // Method 3: JObject access (for JSON loaded containers)
                try
                {
                    var jObject = container as Newtonsoft.Json.Linq.JObject;
                    if (jObject != null && jObject.ContainsKey(propertyName))
                    {
                        var value = jObject[propertyName]?.ToString();
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                            $"GetSafeNoteProperty: Found {propertyName} = '{value}' in JObject");
                        return value ?? fallbackValue;
                    }
                }
                catch { }

                return fallbackValue;
            }
            catch
            {
                return fallbackValue;
            }
        }
        #endregion

        #region Note Editing Behavior - Used by: CreateNoteContent()

    

        /// <summary>
        /// Sets up complete editing behavior with visual feedback and auto-save
        /// Used by: CreateNoteContent() during TextBox setup
        /// Category: Content Management
        /// </summary>
        private static void SetupNoteEditingBehavior(TextBox noteTextBox, dynamic container)
        {
            try
            {
                // Capture the ID immediately so we can always look up the fresh object later
                string containerId = container.Id?.ToString();

                // Auto-save timer to prevent excessive saves during typing (can be disabled via settings)
                System.Windows.Threading.DispatcherTimer autoSaveTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2) // Save 2 seconds after last change
                };

                autoSaveTimer.Tick += (s, e) =>
                {
                    autoSaveTimer.Stop();
                    if (!SettingsManager.DisableNoteAutoSave)
                    {
                        // FIX: Fetch fresh data for saving to ensure we don't overwrite other properties with stale data
                        var freshContainer = ContainerManager.GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId) ?? container;
                        SaveNoteContent(freshContainer, noteTextBox.Text);
                    }
                };

                // CRITICAL: Store original layout properties to maintain anchoring during editing
                Thickness originalMargin = noteTextBox.Margin;
                HorizontalAlignment originalHAlign = noteTextBox.HorizontalAlignment;
                VerticalAlignment originalVAlign = noteTextBox.VerticalAlignment;
                Brush originalTextColor = noteTextBox.Foreground; // Store purely for fallback

                // Mouse enter - very subtle indication it's clickable
                noteTextBox.MouseEnter += (s, e) =>
                {
                    if (!noteTextBox.IsFocused)
                    {
                        noteTextBox.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 70, 130, 180));
                        noteTextBox.Cursor = Cursors.IBeam;
                    }
                };

                // Mouse leave - remove hover effects if not focused
                noteTextBox.MouseLeave += (s, e) =>
                {
                    if (!noteTextBox.IsFocused)
                    {
                        noteTextBox.BorderThickness = new Thickness(0);
                        noteTextBox.BorderBrush = null;
                    }
                };

                Button doneButton = null;

                // Handle focus - show editing state and optional Done button
                noteTextBox.GotFocus += (s, e) =>
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        "TextBox got focus - entering edit mode");

                    // --- FIX START: Get Fresh Data ---
                    // Retrieve the latest container data from the global list to ensure we use the NEW color
                    var freshContainer = ContainerManager.GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId) ?? container;
                    // --- FIX END ---

                    // CRITICAL: Maintain anchoring properties during editing
                    noteTextBox.Margin = originalMargin;
                    noteTextBox.HorizontalAlignment = originalHAlign;
                    noteTextBox.VerticalAlignment = originalVAlign;

                    // Use FRESH container color settings
                    string containerColor = freshContainer.CustomColor?.ToString() ?? SettingsManager.SelectedColor;
                    Color baseColor = Utility.GetColorFromName(containerColor ?? "Gray");

                    // Apply same blending to ALL containers
                    Color highlightedColor = Color.FromRgb(
                        (byte)(baseColor.R * 0.75 + 255 * 0.25),
                        (byte)(baseColor.G * 0.75 + 255 * 0.25),
                        (byte)(baseColor.B * 0.75 + 255 * 0.25));

                    noteTextBox.Background = new SolidColorBrush(highlightedColor) { Opacity = 0.9 };

                    // During edit mode, stick to a standard high-contrast color (Dark Blue) 
                    // or calculate a high-contrast color against the highlight
                    noteTextBox.Foreground = Brushes.DarkBlue;

                    noteTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 130, 180)); // Steel blue
                    noteTextBox.BorderThickness = new Thickness(1);

                    // Create "Done" button if needed
                    if (doneButton == null)
                    {
                        var buttonLayout = GetDoneButtonLayout();
                        doneButton = new Button
                        {
                            Content = Loc.T("✓"),
                            Width = buttonLayout.width,
                            Height = buttonLayout.height,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = buttonLayout.margin,
                            Background = new SolidColorBrush(Color.FromArgb(200, 64, 169, 64)),
                            Foreground = Brushes.White,
                            BorderThickness = new Thickness(0),
                            FontSize = 14,
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            ToolTip = Loc.T("Click to finish editing"),
                            Visibility = Visibility.Collapsed,
                        };

                        // Add click handler for Done button
                        doneButton.Click += (ds, de) => {
                            // Use fresh container for saving here too
                            var currentContainer = ContainerManager.GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId) ?? container;
                            SaveNoteContent(currentContainer, noteTextBox.Text);

                            var parentWindow = FindParentWindow(noteTextBox);
                            if (parentWindow != null) parentWindow.Focus();

                            doneButton.Visibility = Visibility.Collapsed;
                        };

                        // Add button logic (same as before)
                        var buttonParentWindow = FindParentWindow(noteTextBox);
                        if (buttonParentWindow != null)
                        {
                            var border = buttonParentWindow.Content as Border;
                            if (border != null)
                            {
                                Grid overlayGrid = new Grid();
                                var dockPanel = border.Child as DockPanel;
                                if (dockPanel != null)
                                {
                                    border.Child = overlayGrid;
                                    overlayGrid.Children.Add(dockPanel);
                                }
                                overlayGrid.Children.Add(doneButton);
                                Canvas.SetZIndex(doneButton, 1000);
                            }
                        }
                    }

                    doneButton.Visibility = Visibility.Visible;

                    var pWin = FindParentWindow(noteTextBox);
                    if (pWin is NonActivatingWindow naw)
                    {
                        naw.EnableFocusPrevention(false);
                    }
                };

                // Handle focus loss - restore container background
                noteTextBox.LostFocus += (s, e) =>
                {
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                        "TextBox focus lost - restoring background");

                    // --- FIX START: Get Fresh Data ---
                    var freshContainer = ContainerManager.GetContainerData().FirstOrDefault(f => f.Id?.ToString() == containerId) ?? container;
                    // --- FIX END ---

               
                    // Use FRESH container color for the Highlight calculation
                    string containerColor = freshContainer.CustomColor?.ToString() ?? SettingsManager.SelectedColor;
                    Color baseColor = Utility.GetColorFromName(containerColor ?? "Gray");

                    // Calculate Highlight based on the NEW color
                    Color highlightedColor = Color.FromRgb(
                        (byte)(baseColor.R * 0.75 + 255 * 0.25),
                        (byte)(baseColor.G * 0.75 + 255 * 0.25),
                        (byte)(baseColor.B * 0.75 + 255 * 0.25));

                    noteTextBox.Background = new SolidColorBrush(highlightedColor) { Opacity = 0.9 };


                    autoSaveTimer.Stop();
                    if (!SettingsManager.DisableNoteAutoSave)
                    {
                        SaveNoteContent(freshContainer, noteTextBox.Text);
                    }

                    var pWin = FindParentWindow(noteTextBox);
                    if (pWin is NonActivatingWindow naw)
                    {
                        naw.EnableFocusPrevention(true);
                    }

                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => {
                            try
                            {
                                noteTextBox.Margin = originalMargin;
                                noteTextBox.HorizontalAlignment = originalHAlign;
                                noteTextBox.VerticalAlignment = originalVAlign;

                                noteTextBox.BorderThickness = new Thickness(0);
                                noteTextBox.BorderBrush = null;

                                // Apply color scheme using the FRESH container object
                                ApplyNoteColorScheme(noteTextBox, freshContainer);
                            }
                            catch (Exception ex)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                                    $"Error restoring container background: {ex.Message}");
                            }
                        })
                    );
                };

                noteTextBox.TextChanged += (s, e) =>
                {
                    if (noteTextBox.IsFocused && !SettingsManager.DisableNoteAutoSave)
                    {
                        autoSaveTimer.Stop();
                        autoSaveTimer.Start();
                    }
                };

                noteTextBox.ToolTip = Loc.T("Click to edit note content");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error setting up note editing behavior: {ex.Message}");
            }
        }










        /// <summary>
        /// Helper method to find parent NonActivatingWindow
        /// </summary>
        private static Window FindParentWindow(DependencyObject child)
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null)
                return null;

            if (parentObject is Window window)
                return window;

            return FindParentWindow(parentObject);
        }

        /// <summary>
        /// Saves note content to JSON data
        /// Used by: Auto-save timer and focus lost events
        /// Category: Data Persistence
        /// </summary>
        private static void SaveNoteContent(dynamic container, string content)
        {
            try
            {
                string containerId = container.Id?.ToString();
                if (string.IsNullOrEmpty(containerId)) return;

                // Find container in data and update content
                int containerIndex = ContainerDataManager.ContainerData.FindIndex(f => f.Id?.ToString() == containerId);
                if (containerIndex >= 0)
                {
                    dynamic actualContainer = ContainerDataManager.ContainerData[containerIndex];
                    IDictionary<string, object> containerDict = actualContainer as IDictionary<string, object> ??
                        ((JObject)actualContainer).ToObject<IDictionary<string, object>>();

                    containerDict["NoteContent"] = content;
                    ContainerDataManager.ContainerData[containerIndex] = JObject.FromObject(containerDict);
                    ContainerDataManager.SaveContainerData();

                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerUpdate,
                        $"Auto-saved note content for container '{container.Title}' ({content.Length} characters)");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerUpdate,
                    $"Error saving note content: {ex.Message}");
            }
        }
        #endregion

        #region Context Menu Creation - Used by: ContainerManager context menu logic

        /// <summary>
        /// Creates Note container specific context menu items
        /// Used by: ContainerManager context menu creation for Note containers
        /// Category: UI Context Menu
        /// </summary>
        public static void AddNoteContextMenuItems(ContextMenu menu, dynamic container, TextBox noteTextBox)
        {
            try
            {
                
            

                //        // Text formatting submenu
                //    //    MenuItem formatMenu = new MenuItem { Header = Loc.T("Text Format") };

                //        // Font Size submenu
                //        MenuItem fontSizeMenu = new MenuItem { Header = Loc.T("Font Size") };
                //        foreach (string size in new[] { "Small", "Medium", "Large", "Extra Large" })
                //        {
                //            MenuItem sizeItem = new MenuItem { Header = size };
                //            sizeItem.Click += (s, e) => ChangeNoteFontSize(container, noteTextBox, size);
                //            fontSizeMenu.Items.Add(sizeItem);
                //        }
                // //       formatMenu.Items.Add(fontSizeMenu);

                //        // Font Family submenu
                //        MenuItem fontFamilyMenu = new MenuItem { Header = Loc.T("Font Family") };
                //        foreach (string family in new[] { "Segoe UI", "Consolas", "Times New Roman", "Arial", "Courier New" })
                //        {
                //            MenuItem familyItem = new MenuItem { Header = family };
                //            familyItem.Click += (s, e) => ChangeNoteFontFamily(container, noteTextBox, family);
                //            fontFamilyMenu.Items.Add(familyItem);
                //        }
                // //       formatMenu.Items.Add(fontFamilyMenu);

                //        // Text Color submenu (uses same colors as container icons)
                //        MenuItem textColorMenu = new MenuItem { Header = Loc.T("Text Color") };
                //        foreach (string color in new[] { "Default", "Red", "Green", "Blue", "White", "Black", "Gray" })
                //        {
                //            MenuItem colorItem = new MenuItem { Header = color };
                //            colorItem.Click += (s, e) => ChangeNoteTextColor(container, noteTextBox, color == "Default" ? null : color);
                //            textColorMenu.Items.Add(colorItem);
                //        }
                ////        formatMenu.Items.Add(textColorMenu);

                // //       menu.Items.Add(formatMenu);

                // Text operations
                //   MenuItem textOpsMenu = new MenuItem { Header = Loc.T("Text Operations") };


                // Text Format form (new unified approach)
                MenuItem textFormatFormItem = new MenuItem { Header = Loc.T("Text Format...") };
                textFormatFormItem.Click += (s, e) =>
                {
                    try
                    {
                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                            $"Opening Text Format form for container '{container.Title}'");
                        TextFormatFormManager.ShowTextFormatForm(container, noteTextBox);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                            $"Error opening Text Format form: {ex.Message}");
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Error opening Text Format form: {ex.Message}", "Form Error");
                    }
                };
                menu.Items.Add(textFormatFormItem);

                // A seperator to commnds to note-specific commands
                menu.Items.Add(new Separator());

                MenuItem copyAllItem = new MenuItem { Header = Loc.T("Copy All Text") };
                copyAllItem.Click += (s, e) => CopyAllNoteText(noteTextBox);
                menu.Items.Add(copyAllItem);

                MenuItem clearAllItem = new MenuItem { Header = Loc.T("Clear All Text") };
                clearAllItem.Click += (s, e) => ClearAllNoteText(container, noteTextBox);
                menu.Items.Add(clearAllItem);

           //     textOpsMenu.Items.Add(new Separator());

                MenuItem wordWrapItem = new MenuItem
                {
                    Header = GetWordWrapMenuText(container.WordWrap?.ToString())
                };
                wordWrapItem.Click += (s, e) => ToggleWordWrap(container, noteTextBox);
         //       textOpsMenu.Items.Add(wordWrapItem);

                MenuItem spellCheckItem = new MenuItem
                {
                    Header = GetSpellCheckMenuText(container.SpellCheck?.ToString())
                };
                spellCheckItem.Click += (s, e) => ToggleSpellCheck(container, noteTextBox);
             //   textOpsMenu.Items.Add(spellCheckItem);

             //   menu.Items.Add(textOpsMenu);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    "Added Note container context menu items");

                // Keep existing individual submenu for backwards compatibility



            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error creating note context menu: {ex.Message}");
            }
        }
        #endregion

        /// <summary>
        /// Creates a subtle background specifically for text area to improve visibility
        /// </summary>
        private static Brush GetTextBackgroundBrush(dynamic container)
        {
            try
            {
                // Get the container color name safely
                string containerColorName = null;
                try { containerColorName = container.CustomColor?.ToString(); } catch { }

                if (string.IsNullOrEmpty(containerColorName))
                    containerColorName = SettingsManager.SelectedColor ?? "Gray";

                // Use the shared helper to determine the actual visual background color
                Color actualBg = GetActualContainerBackgroundColor(containerColorName);
                double luminance = GetRelativeLuminance(actualBg);

                if (luminance > 0.5) // Light background
                {
                    // Dark semi-transparent background for light containers
                    return new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                }
                else // Dark background
                {
                    // Light semi-transparent background for dark containers
                    return new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
                }
            }
            catch
            {
                return Brushes.Transparent;
            }
        }





        #region Text Formatting Methods - Used by: Context menu actions

        /// <summary>
        /// Changes the font size of the note content
        /// </summary>
        private static void ChangeNoteFontSize(dynamic container, TextBox noteTextBox, string size)
        {
            try
            {
                double fontSize = GetNoteFontSizeValue(size);
                noteTextBox.FontSize = fontSize;

                // Save to container data
                string containerId = container.Id?.ToString();
                if (!string.IsNullOrEmpty(containerId))
                {
                    ContainerManager.UpdateContainerProperty(container, "NoteFontSize", size,
                        $"Changed font size to {size}");
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Changed note font size to {size} for container '{container.Title}'");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error changing font size: {ex.Message}");
            }
        }

        /// <summary>
        /// Changes the font family of the note content
        /// </summary>
        private static void ChangeNoteFontFamily(dynamic container, TextBox noteTextBox, string family)
        {
            try
            {
                noteTextBox.FontFamily = new FontFamily(family);

                // Save to container data
                string containerId = container.Id?.ToString();
                if (!string.IsNullOrEmpty(containerId))
                {
                    ContainerManager.UpdateContainerProperty(container, "NoteFontFamily", family,
                        $"Changed font family to {family}");
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Changed note font family to {family} for container '{container.Title}'");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error changing font family: {ex.Message}");
            }
        }

        /// <summary>
        /// Changes the text color of the note content (uses container TextColor property)
        /// </summary>
        private static void ChangeNoteTextColor(dynamic container, TextBox noteTextBox, string colorName)
        {
            try
            {
                // Update the container TextColor property (same as icons use)
                string containerId = container.Id?.ToString();
                if (!string.IsNullOrEmpty(containerId))
                {
                    ContainerManager.UpdateContainerProperty(container, "TextColor", colorName,
                        $"Changed text color to {colorName}");
                }

                // Apply the color to the TextBox immediately
                if (!string.IsNullOrEmpty(colorName) && colorName != "Default")
                {
                    var textColor = Utility.GetColorFromName(colorName);
                    noteTextBox.Foreground = new SolidColorBrush(textColor);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        $"Applied text color {colorName} to TextBox immediately");
                }
                else
                {
                    noteTextBox.Foreground = Brushes.White; // Default
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        "Applied default white text color to TextBox");
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Changed note text color to {colorName} for container '{container.Title}'");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error changing text color: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies all text from the note to clipboard
        /// </summary>
        private static void CopyAllNoteText(TextBox noteTextBox)
        {
            try
            {
                if (!string.IsNullOrEmpty(noteTextBox.Text))
                {
                    Clipboard.SetText(noteTextBox.Text);
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        "Copied all note text to clipboard");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error copying note text: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all text from the note
        /// </summary>
        private static void ClearAllNoteText(dynamic container, TextBox noteTextBox)
        {
            try
            {
                if (MessageBox.Show("Are you sure you want to clear all text from this note?",
                    "Clear Note", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    noteTextBox.Text = "";
                    SaveNoteContent(container, "");

                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                        $"Cleared all text from note container '{container.Title}'");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error clearing note text: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles word wrap setting for the note
        /// </summary>
        private static void ToggleWordWrap(dynamic container, TextBox noteTextBox)
        {
            try
            {
                bool currentWrap = noteTextBox.TextWrapping == TextWrapping.Wrap;
                bool newWrap = !currentWrap;

                noteTextBox.TextWrapping = newWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

                // Save to container data
                string containerId = container.Id?.ToString();
                if (!string.IsNullOrEmpty(containerId))
                {
                    ContainerManager.UpdateContainerProperty(container, "WordWrap", newWrap.ToString().ToLower(),
                        $"Toggled word wrap to {newWrap}");
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Toggled word wrap to {newWrap} for container '{container.Title}'");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error toggling word wrap: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggles spell check setting for the note
        /// </summary>
        private static void ToggleSpellCheck(dynamic container, TextBox noteTextBox)
        {
            try
            {
                bool currentSpellCheck = noteTextBox.SpellCheck.IsEnabled;
                bool newSpellCheck = !currentSpellCheck;

                noteTextBox.SpellCheck.IsEnabled = newSpellCheck;

                // Save to container data
                string containerId = container.Id?.ToString();
                if (!string.IsNullOrEmpty(containerId))
                {
                    ContainerManager.UpdateContainerProperty(container, "SpellCheck", newSpellCheck.ToString().ToLower(),
                        $"Toggled spell check to {newSpellCheck}");
                }

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    $"Toggled spell check to {newSpellCheck} for container '{container.Title}'");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error toggling spell check: {ex.Message}");
            }
        }
        #endregion

        #region Helper Methods - Internal formatting and utility functions

        /// <summary>
        /// Gets TextWrapping enum from string value
        /// </summary>
        private static TextWrapping GetTextWrapping(string wordWrap)
        {
            return wordWrap?.ToLower() == "false" ? TextWrapping.NoWrap : TextWrapping.Wrap;
        }

        /// <summary>
        /// Gets spell check boolean from string value
        /// </summary>
        private static bool GetSpellCheck(string spellCheck)
        {
            return spellCheck?.ToLower() != "false"; // Default to true
        }

        


        public static Brush GetNoteForeground(dynamic container)
        {
            try
            {
                // Try multiple ways to get the TextColor property
                string textColorName = null;
                // Method 1: Direct property access
                try
                {
                    textColorName = container.TextColor?.ToString();
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // TextColor property doesn't exist - will use default
                }
                // Method 2: Dictionary access if container is a dictionary
                if (string.IsNullOrEmpty(textColorName))
                {
                    try
                    {
                        var containerDict = container as IDictionary<string, object>;
                        if (containerDict != null && containerDict.ContainsKey("TextColor"))
                        {
                            textColorName = containerDict["TextColor"]?.ToString();
                        }
                    }
                    catch { }
                }

                // Get container background color for contrast checking
                string containerColorName = null;
                try
                {
                    containerColorName = container.CustomColor?.ToString();
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // CustomColor property doesn't exist
                }
                // Try dictionary access for container color too
                if (string.IsNullOrEmpty(containerColorName))
                {
                    try
                    {
                        var containerDict = container as IDictionary<string, object>;
                        if (containerDict != null && containerDict.ContainsKey("CustomColor"))
                        {
                            containerColorName = containerDict["CustomColor"]?.ToString();
                        }
                    }
                    catch { }
                }
                // Fallback to SettingsManager if no container-specific color
                if (string.IsNullOrEmpty(containerColorName))
                {
                    containerColorName = SettingsManager.SelectedColor;
                }

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Note container TextColor: '{textColorName ?? "null"}', ContainerColor: '{containerColorName ?? "null"}'");

                // Get the actual tinted container background color for contrast calculation
                Color actualContainerBackground = GetActualContainerBackgroundColor(containerColorName);

                // If text color is specified, check if it has good contrast with the tinted container background
                if (!string.IsNullOrEmpty(textColorName) && textColorName != "null")
                {
                    Color originalTextColor = Utility.GetColorFromName(textColorName);

                    // Calculate contrast ratio between text and actual container background
                    double contrastRatio = CalculateContrastRatio(originalTextColor, actualContainerBackground);

                    // ALWAYS use the user's chosen color - never change it
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        $"Applied user's chosen text color: {textColorName} (contrast ratio: {contrastRatio:F2})");
                    //return new SolidColorBrush(originalTextColor);
                    // Use bright/vibrant version of user's chosen color for text
                    Color brightTextColor = GetBrightTextVersion(textColorName);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                        $"Applied bright text version: {textColorName} -> bright variant (contrast ratio: {contrastRatio:F2})");
                    return new SolidColorBrush(brightTextColor);

                }

                // No text color specified - use smart default based on container background
                string smartDefault = GetSmartDefaultTextColor(actualContainerBackground);
                var defaultColor = Utility.GetColorFromName(smartDefault);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Using smart default text color: {smartDefault} for container background");

                return new SolidColorBrush(defaultColor);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Error getting note text color, using white: {ex.Message}");
                return Brushes.White; // Safe fallback
            }
        }


        /// <summary>
        /// Converts container background colors to bright/vibrant text versions
        /// </summary>


        private static Color GetBrightTextVersion(string colorName)
        {
            // High-contrast bright text versions
            return colorName switch
            {
                "Red" => Color.FromRgb(255, 10, 10),        // Bright red with slight warmth
                "Green" => Color.FromRgb(10, 255, 10),      // Bright green with slight warmth
                "Blue" => Color.FromRgb(50, 80, 255),     // Bright blue with better readability
                "Purple" => Color.FromRgb(191, 0, 191),   // Bright purple with better contrast
                "Orange" => Color.FromRgb(230, 136, 50),    // Bright orange
                "Yellow" => Color.FromRgb(255, 255, 2),   // Bright yellow with slight depth
                "Fuchsia" => Color.FromRgb(248, 67, 250),   // Hot pink
                "Teal" => Color.FromRgb(25, 255, 194),      // Bright teal
                "White" => Color.FromRgb(237, 237, 237),    // Pure white
                "Black" => Color.FromRgb(23, 26, 25),          // Pure black
                "Gray" => Color.FromRgb(170, 170, 170),     // Light gray
                "Beige" => Color.FromRgb(210, 144, 14),    // Bright beige
                "Bismark" => Color.FromRgb(0, 135, 224),  // Bright blue-gray
                _ => Utility.GetColorFromName(colorName)
            };
        }

        /// <summary>
        /// Gets the actual tinted container background color as it appears visually
        /// </summary>
        private static Color GetActualContainerBackgroundColor(string containerColorName)
        {
            if (string.IsNullOrEmpty(containerColorName))
                return Colors.Transparent;

            Color baseColor = Utility.GetColorFromName(containerColorName);

            // Apply the same tint logic as the container uses
            if (SettingsManager.TintValue > 0)
            {
                // Simulate the visual effect of tinted color over transparent background
                double opacity = SettingsManager.TintValue / 100.0;
                // Blend with white background (common desktop color)
                return Color.FromRgb(
                    (byte)(baseColor.R * opacity + 255 * (1 - opacity)),
                    (byte)(baseColor.G * opacity + 255 * (1 - opacity)),
                    (byte)(baseColor.B * opacity + 255 * (1 - opacity))
                );
            }

            // No tint - assume white/transparent background
            return Colors.White;
        }

        /// <summary>
        /// Calculates contrast ratio between two colors (WCAG standard)
        /// </summary>
        private static double CalculateContrastRatio(Color foreground, Color background)
        {
            double fgLuminance = GetRelativeLuminance(foreground);
            double bgLuminance = GetRelativeLuminance(background);

            double lighter = Math.Max(fgLuminance, bgLuminance);
            double darker = Math.Min(fgLuminance, bgLuminance);

            return (lighter + 0.05) / (darker + 0.05);
        }

        /// <summary>
        /// Gets relative luminance of a color (WCAG standard)
        /// </summary>
        private static double GetRelativeLuminance(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

 
     
        /// <summary>
        /// Gets smart default text color based on container background
        /// </summary>
        private static string GetSmartDefaultTextColor(Color containerBackground)
        {
            double luminance = GetRelativeLuminance(containerBackground);
            return luminance > 0.5 ? "Black" : "White";
        }



        /// <summary>
        /// Gets font size value from string
        /// </summary>
        private static double GetNoteFontSize(string fontSize)
        {
            double result;
            switch (fontSize?.ToLower())
            {
                case "small": return 11;
                case "large": return 16;
                case "extra large": return 20;
                default: return 14; // Medium

                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
        $"GetNoteFontSize: '{fontSize}' -> {result}");
                    return result;

            }
        }

        /// <summary>
        /// Gets font size value for setting changes
        /// </summary>
        public static double GetNoteFontSizeValue(string size)
        {
            return GetNoteFontSize(size);
        }

        /// <summary>
        /// Gets FontFamily from string
        /// </summary>
        private static FontFamily GetNoteFontFamily(string fontFamily)
        {
            try
            {
                return new FontFamily(fontFamily ?? "Segoe UI");
            }
            catch
            {
                return new FontFamily("Segoe UI"); // Fallback
            }
        }

        /// <summary>
        /// Applies color scheme to note background based on container colors
        /// </summary>
        /// <summary>
        /// Applies color scheme to note background AND FOREGROUND based on container colors
        /// </summary>
        private static void ApplyNoteColorScheme(TextBox noteTextBox, dynamic container)
        {
            try
            {
                // 1. BACKGROUND LOGIC
                string containerColor = container.CustomColor?.ToString() ?? SettingsManager.SelectedColor;

                if (containerColor != null && containerColor != "Default")
                {
                    Color baseColor = Utility.GetColorFromName(containerColor);
                    if (SettingsManager.TintValue > 0)
                    {
                        var tintedBrush = new SolidColorBrush(baseColor) { Opacity = SettingsManager.TintValue / 100.0 };
                        noteTextBox.Background = tintedBrush;
                    }
                    else
                    {
                        noteTextBox.Background = Brushes.Transparent;
                    }
                }
                else
                {
                    noteTextBox.Background = Brushes.Transparent;
                }

                // 2. BORDER LOGIC
                noteTextBox.BorderThickness = new Thickness(0);
                noteTextBox.BorderBrush = null;

                // 3. FOREGROUND LOGIC (THE CRITICAL FIX)
                // Force text color recalculation based on the new background
                noteTextBox.Foreground = GetNoteForeground(container);

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    $"Applied note scheme: BG={containerColor}, FG={noteTextBox.Foreground}");
            }
            catch (Exception ex)
            {
                // Safe defaults
                noteTextBox.Background = Brushes.Transparent;
                noteTextBox.Foreground = Brushes.White;
            }
        }


        /// <summary>
        /// Publicly accessible method to force a visual refresh of the note
        /// Call this from ContainerManager.UpdateContainerProperty when CustomColor changes
        /// </summary>
        public static void RefreshNoteVisuals(dynamic container, TextBox noteTextBox)
        {
            if (noteTextBox == null) return;

            // Re-apply the full color scheme (Background + Foreground)
            ApplyNoteColorScheme(noteTextBox, container);
        }

        /// <summary>
        /// Gets word wrap menu text based on current state
        /// </summary>
        private static string GetWordWrapMenuText(string currentState)
        {
            bool isEnabled = currentState?.ToLower() != "false";
            return isEnabled ? "✓ Word Wrap" : "Word Wrap";
        }

        /// <summary>
        /// Gets spell check menu text based on current state
        /// </summary>
        private static string GetSpellCheckMenuText(string currentState)
        {
            bool isEnabled = currentState?.ToLower() != "false";
            return isEnabled ? "✓ Spell Check" : "Spell Check";
        }
        #endregion

        #region Note Container Validation - Used by: Data validation

        /// <summary>
        /// Validates if a container is a Note type container
        /// Used by: Various container operations to check container type
        /// Category: Validation
        /// </summary>
        public static bool IsNoteContainer(dynamic container)
        {
            return container?.ItemsType?.ToString() == "Note";
        }

        /// <summary>
        /// Gets default Note container properties for new container creation
        /// Used by: ContainerDataManager.ApplyContainerDefaults() for Note containers
        /// Category: Default Values
        /// </summary>
        public static void ApplyNoteDefaults(IDictionary<string, object> containerDict)
        {
            try
            {
                containerDict["NoteContent"] = "";
                containerDict["NoteFontSize"] = "Medium";
                containerDict["NoteFontFamily"] = "Segoe UI";
                containerDict["WordWrap"] = "true";
                containerDict["SpellCheck"] = "true";

                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.ContainerCreation,
                    "Applied default properties for Note container");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.ContainerCreation,
                    $"Error applying note defaults: {ex.Message}");
            }
        }
        #endregion

        #region Tweakable Layout Configuration - Adjust these values for perfect positioning

        
        /// <summary>
        /// TWEAK THESE VALUES: Text spacing inside the TextBox
        /// </summary>
        private static Thickness GetTextBoxPadding()
        {
            double leftPadding = 6;   // TWEAK: Text distance from TextBox left edge
            double topPadding = 4;    // TWEAK: Text distance from TextBox top edge  
            double rightPadding = 6;  // TWEAK: Text distance from TextBox right edge
            double bottomPadding = 4; // TWEAK: Text distance from TextBox bottom edge

            return new Thickness(leftPadding, topPadding, rightPadding, bottomPadding);
        }
        private static Thickness GetTextBoxMargin()
        {
            double leftMargin = 8;   // TWEAK: Equal distance from all container edges
            double rightMargin = 24;
            double topMargin = 4;     // TWEAK: Distance from top (below title)
            double bottomMargin = 28; // TWEAK: Space for Done button below textbox

            return new Thickness(leftMargin, topMargin, rightMargin, bottomMargin);
        }
        /// <summary>
        /// TWEAK THESE VALUES: TextBox border appearance
        /// </summary>
        private static (Thickness thickness, Brush brush) GetTextBoxBorder()
        {
            double borderWidth = 1;   // TWEAK: Border thickness (0=no border, 1=thin, 2=thick)

            // TWEAK: Border color - adjust Alpha for transparency
            Color borderColor = Color.FromArgb(
                80,    // TWEAK: Alpha (0=invisible, 255=solid)
                100,   // TWEAK: Red component
                100,   // TWEAK: Green component  
                100    // TWEAK: Blue component
            );

            return (new Thickness(borderWidth), new SolidColorBrush(borderColor));
        }

        /// <summary>
        /// TWEAK THESE VALUES: Done button positioning
        /// </summary>
        private static (double width, double height, Thickness margin) GetDoneButtonLayout()
        {
            double buttonWidth = 20;   // TWEAK: Done button width
            double buttonHeight = 20;  // TWEAK: Done button height

            double rightMargin = 24;    // TWEAK: Distance from right container edge (matches textbox margin)
            double bottomMargin = 4;   // TWEAK: Distance from bottom container edge

            return (buttonWidth, buttonHeight, new Thickness(0, 0, rightMargin, bottomMargin));
        }
        #endregion

    }
}