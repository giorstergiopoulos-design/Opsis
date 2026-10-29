using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IWshRuntimeLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpsisContainers
{
    /// <summary>
    /// Handles all item move operations with modern, tab-aware dialog interface.
    /// Separated from ContainerManager for better code organization and maintainability.
    /// </summary>
    public static class ItemMoveDialog
    {
        #region Public Methods

        /// <summary>
        /// Shows the modern tab-aware Move dialog and handles the complete move operation
        /// </summary>
        /// <param name="item">The item to move</param>
        /// <param name="sourceContainer">The source container containing the item</param>
        /// <param name="dispatcher">Dispatcher for UI operations</param>



        private static SolidColorBrush GetAccentColorBrush()
        {
            try
            {
                string selectedColorName = SettingsManager.SelectedColor;
                var mediaColor = Utility.GetColorFromName(selectedColorName);
                return new SolidColorBrush(mediaColor);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"Error getting accent color: {ex.Message}");
                // Fallback to blue
                return new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 133, 244));
            }
        }


        public static void ShowMoveDialog(dynamic item, dynamic sourceContainer, Dispatcher dispatcher)
        {
            // Create modern hierarchical Move dialog
            var moveWindow = new Window
            {
                Title = Loc.T("Move Item To..."),
                Width = 480,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250)),
                AllowsTransparency = true
            };

            // Get item details for display
            IDictionary<string, object> itemDict = item is IDictionary<string, object> dict ?
                dict : ((JObject)item).ToObject<IDictionary<string, object>>();
            string itemName = itemDict.ContainsKey("DisplayName") ?
                itemDict["DisplayName"].ToString() :
                System.IO.Path.GetFileNameWithoutExtension(itemDict.ContainsKey("Filename") ? itemDict["Filename"].ToString() : "Unknown");

            // Main container with modern styling
            Border mainBorder = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Effect = new DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    Direction = 315,
                    ShadowDepth = 2,
                    BlurRadius = 8,
                    Opacity = 0.2
                }
            };

            Grid rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // HEADER: Modern title bar with close button
       

            Border headerBorder = new Border
            {
                Background = GetAccentColorBrush(), // Use user's selected theme color
                CornerRadius = new CornerRadius(0, 0, 0, 0), // Match the window's corner radius
                Padding = new Thickness(20, 15, 15, 15)
            };

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Title with item name
            StackPanel titlePanel = new StackPanel();

            TextBlock mainTitle = new TextBlock
            {
                Text = Loc.T("Move Item To..."),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 2)
            };

            TextBlock subTitle = new TextBlock
            {
                Text = $"Moving: {itemName}",
                FontSize = 12,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)),
                FontStyle = FontStyles.Italic
            };

            titlePanel.Children.Add(mainTitle);
            titlePanel.Children.Add(subTitle);

            Button closeButton = new Button
            {
                Content = Loc.T("✕"),
                Width = 30,
                Height = 30,
                FontSize = 16,
                FontWeight = FontWeights.Normal,
                Foreground = System.Windows.Media.Brushes.White,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            closeButton.Click += (s, e) => moveWindow.Close();

            headerGrid.Children.Add(titlePanel);
            headerGrid.Children.Add(closeButton);
            Grid.SetColumn(closeButton, 1);
            // Add drag functionality to header area only (like EditShortcutWindow)
            headerBorder.MouseLeftButtonDown += (sender, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    moveWindow.DragMove();
                }
            };

            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            rootGrid.Children.Add(headerBorder);

            // CONTENT: Hierarchical container and tab list
            Border contentBorder = new Border
            {
                Padding = new Thickness(0),
                Background = System.Windows.Media.Brushes.White
            };

            // Instructions
            TextBlock instructionsText = new TextBlock
            {
                Text = Loc.T("Select a destination container or tab:"),
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(95, 99, 104)),
                Margin = new Thickness(20, 15, 20, 10)
            };

            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 400, // Explicit height limit to ensure scrolling activates
                Margin = new Thickness(20, 0, 20, 0),
                    CanContentScroll = true // Improve scrolling performance
            };

            StackPanel targetsPanel = new StackPanel();

            // Build hierarchical target list
            BuildTargetsList(targetsPanel, sourceContainer, item, moveWindow);

            scrollViewer.Content = targetsPanel;

            StackPanel contentStack = new StackPanel();
            contentStack.Children.Add(instructionsText);
            contentStack.Children.Add(scrollViewer);
            contentBorder.Child = contentStack;
            Grid.SetRow(contentBorder, 1);
            rootGrid.Children.Add(contentBorder);

            // FOOTER: Cancel button
            Border footerBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 15, 20, 15),
                CornerRadius = new CornerRadius(0, 0, 12, 12)
            };

            Button cancelButton = new Button
            {
                Content = Loc.T("Cancel"),
                Width = 100,
                Height = 36,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(95, 99, 104)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
               // CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            cancelButton.Click += (s, e) => moveWindow.Close();

            footerBorder.Child = cancelButton;
            Grid.SetRow(footerBorder, 2);
            rootGrid.Children.Add(footerBorder);

            mainBorder.Child = rootGrid;
            // Add drag functionality like EditShortcutWindow
            moveWindow.MouseLeftButtonDown += (sender, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    moveWindow.DragMove();
                }
            };

            moveWindow.Content = mainBorder;
            moveWindow.ShowDialog();
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Builds the hierarchical list of move targets (containers and tabs)
        /// Updated to allow moving items between tabs within the SAME container.
        /// </summary>
        private static void BuildTargetsList(StackPanel targetsPanel, dynamic sourceContainer, dynamic item, Window moveWindow)
        {
            string sourceContainerId = sourceContainer.Id?.ToString();

            // Determine source state to filter out the "Current" location
            bool sourceHasTabs = sourceContainer.TabsEnabled?.ToString().ToLower() == "true";
            int sourceActiveTab = -1;
            if (sourceHasTabs)
            {
                sourceActiveTab = Convert.ToInt32(sourceContainer.CurrentTab?.ToString() ?? "0");
            }

            var containerData = ContainerManager.GetContainerData();

            foreach (var container in containerData)
            {
                // 1. Global Exclusions: Portal and Note Containers cannot receive items via this dialog
                if (container.ItemsType?.ToString() == "Portal" || container.ItemsType?.ToString() == "Note")
                    continue;

                string currentContainerId = container.Id?.ToString();
                bool isSourceContainer = (currentContainerId == sourceContainerId);
                bool containerHasTabs = container.TabsEnabled?.ToString().ToLower() == "true";

                // 2. Source Container Logic:
                // If this is the source container AND it doesn't have tabs, skip it entirely 
                if (isSourceContainer && !containerHasTabs)
                    continue;

                string containerTitle = container.Title?.ToString() ?? "Unnamed Container";

                if (!containerHasTabs)
                {
                    // Regular container - single target
                    CreateMoveTargetButton(targetsPanel, containerTitle, "🏠", container, null, item, sourceContainer, moveWindow, false);
                }
                else
                {
                    // Tabbed container - show parent container + tabs

                    // Parent container header (non-clickable info)
                    Border containerHeaderBorder = new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250)),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 220, 224)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Margin = new Thickness(0, 5, 0, 2),
                        Padding = new Thickness(12, 8, 12, 8)
                    };

                    StackPanel containerHeaderPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    TextBlock containerIcon = new TextBlock
                    {
                        Text = Loc.T("📂"),
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    TextBlock containerNameText = new TextBlock
                    {
                        Text = containerTitle,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 33, 36)),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    // FIX: Smart label indicating omitted tab
                    int tCount = GetTabCount(container);
                    string countLabel = $"({tCount} tabs)";
                    if (isSourceContainer)
                    {
                        countLabel += " 1 omitted";
                    }

                    TextBlock tabCountText = new TextBlock
                    {
                        Text = countLabel,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(95, 99, 104)),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    containerHeaderPanel.Children.Add(containerIcon);
                    containerHeaderPanel.Children.Add(containerNameText);
                    containerHeaderPanel.Children.Add(tabCountText);
                    containerHeaderBorder.Child = containerHeaderPanel;
                    targetsPanel.Children.Add(containerHeaderBorder);

                    // Option to move to container "Main Items" (Hidden storage)
                    // We HIDE this if it is the Source Container, to prevent confusion
                    if (!isSourceContainer)
                    {
                        CreateMoveTargetButton(targetsPanel, "📋 Main Items", "↳", container, null, item, sourceContainer, moveWindow, true);
                    }

                    // Individual tabs
                    var tabs = container.Tabs as JArray ?? new JArray();
                    for (int i = 0; i < tabs.Count; i++)
                    {
                        // EXCLUSION LOGIC:
                        // If this is the Source Container AND this is the Active Tab, skip it.
                        if (isSourceContainer && i == sourceActiveTab)
                            continue;

                        var tab = tabs[i] as JObject;
                        if (tab != null)
                        {
                            string tabName = tab["TabName"]?.ToString() ?? $"Tab {i}";
                            CreateMoveTargetButton(targetsPanel, $"📄 {tabName}", "↳", container, i, item, sourceContainer, moveWindow, true);
                        }
                    }

                    // Add separator after tabbed container group
                    Border separator = new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230)),
                        Margin = new Thickness(0, 8, 0, 8)
                    };
                    targetsPanel.Children.Add(separator);
                }
            }
        }

        /// <summary>
        /// Gets the number of tabs for a container (for display purposes)
        /// </summary>
        private static int GetTabCount(dynamic container)
        {
            try
            {
                var tabs = container.Tabs as JArray;
                return tabs?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Creates a clickable move target button for container or tab
        /// </summary>
        private static void CreateMoveTargetButton(StackPanel parent, string displayText, string prefix,
            dynamic targetContainer, int? targetTabIndex, dynamic item, dynamic sourceContainer, Window moveWindow, bool isIndented)
        {
            Button targetButton = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
           //     CornerRadius = new CornerRadius(6),
                Margin = new Thickness(isIndented ? 20 : 0, 2, 0, 2),
                Padding = new Thickness(12, 10, 12, 10),
                Cursor = Cursors.Hand,
                MinHeight = 40
            };

            StackPanel buttonContent = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            if (isIndented)
            {
                TextBlock indentPrefix = new TextBlock
                {
                    Text = prefix,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 120, 120)),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                buttonContent.Children.Add(indentPrefix);
            }

            TextBlock targetText = new TextBlock
            {
                Text = displayText,
                FontSize = 13,
                FontWeight = isIndented ? FontWeights.Normal : FontWeights.Medium,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 33, 36)),
                VerticalAlignment = VerticalAlignment.Center
            };

            buttonContent.Children.Add(targetText);
            targetButton.Content = buttonContent;

            // Add hover effects
            targetButton.MouseEnter += (s, e) =>
            {
                targetButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
                targetButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 133, 244));
            };

            targetButton.MouseLeave += (s, e) =>
            {
                targetButton.Background = System.Windows.Media.Brushes.White;
                targetButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(218, 220, 224));
            };

            // Handle click - perform the actual move
            targetButton.Click += (s, e) => HandleMoveToTarget(item, sourceContainer, targetContainer, targetTabIndex, moveWindow);

            parent.Children.Add(targetButton);
        }

        /// <summary>
        /// Handles the actual move operation to container or tab
        /// </summary>
        private static void HandleMoveToTarget(dynamic item, dynamic sourceContainer, dynamic targetContainer,
            int? targetTabIndex, Window moveWindow)
        {
            try
            {
                IDictionary<string, object> itemDict = item is IDictionary<string, object> dict ?
                    dict : ((JObject)item).ToObject<IDictionary<string, object>>();
                string filename = itemDict.ContainsKey("Filename") ? itemDict["Filename"].ToString() : "Unknown";

                // Determine source location (main Items or tab)
                JArray sourceItems = null;
                bool sourceIsTabbed = sourceContainer.TabsEnabled?.ToString().ToLower() == "true";

                if (sourceIsTabbed)
                {
                    var sourceTabs = sourceContainer.Tabs as JArray ?? new JArray();
                    int sourceCurrentTab = Convert.ToInt32(sourceContainer.CurrentTab?.ToString() ?? "0");
                    if (sourceCurrentTab >= 0 && sourceCurrentTab < sourceTabs.Count)
                    {
                        var sourceActiveTab = sourceTabs[sourceCurrentTab] as JObject;
                        sourceItems = sourceActiveTab?["Items"] as JArray ?? new JArray();
                    }
                }
                else
                {
                    sourceItems = sourceContainer.Items as JArray ?? new JArray();
                }

                // Find item in source
                var itemToMove = sourceItems?.FirstOrDefault(i => i["Filename"]?.ToString() == filename);
                if (itemToMove == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling,
                        $"Item '{filename}' not found in source location");
                    return;
                }

                // Determine destination location
                JArray destItems = null;
                string destinationDescription = "";

                if (targetTabIndex.HasValue)
                {
                    // Moving to specific tab
                    var targetTabs = targetContainer.Tabs as JArray ?? new JArray();
                    if (targetTabIndex.Value >= 0 && targetTabIndex.Value < targetTabs.Count)
                    {
                        var targetTab = targetTabs[targetTabIndex.Value] as JObject;
                        destItems = targetTab?["Items"] as JArray ?? new JArray();
                        string tabName = targetTab?["TabName"]?.ToString() ?? $"Tab {targetTabIndex.Value}";
                        destinationDescription = $"tab '{tabName}' in container '{targetContainer.Title}'";
                    }
                }
                else
                {
                    // Moving to container main Items
                    destItems = targetContainer.Items as JArray ?? new JArray();
                    destinationDescription = $"main area of container '{targetContainer.Title}'";
                }

                if (destItems == null)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling,
                        "Could not determine destination location");
                    return;
                }

                // Perform the move
                sourceItems.Remove(itemToMove);
                destItems.Add(itemToMove);

                // Save changes
                ContainerDataManager.SaveContainerData();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.IconHandling,
                    $"Successfully moved item '{filename}' from '{sourceContainer.Title}' to {destinationDescription}");

                moveWindow.Close();

                // Show success feedback and refresh UI
                // Show success feedback and refresh UI
                ShowMoveSuccessAndRefresh(filename, destinationDescription, sourceContainer, targetContainer);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.IconHandling,
                    $"Error during move operation: {ex.Message}");
                MessageBoxesManager.ShowOKOnlyMessageBoxForm($"Move failed: {ex.Message}", "Error");
            }
        }

        /// <summary>
        /// Shows success message and refreshes UI after move
        /// </summary>
        /// <summary>
        /// Shows success message and refreshes UI after move - only affects source and target containers
        /// </summary>
        private static void ShowMoveSuccessAndRefresh(string itemName, string destination, dynamic sourceContainer, dynamic targetContainer)
        {
            //// Show brief success notification
            //MessageBoxesManager.ShowOKOnlyMessageBoxForm(
            //    $"'{itemName}' moved successfully to {destination}.",
            //    "Move Complete");

            // Reload only the source and target containers to prevent duplicates
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ContainerManager.ReloadSpecificContainers(sourceContainer, targetContainer);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                        $"Error refreshing specific containers after move: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        #endregion
    }
}