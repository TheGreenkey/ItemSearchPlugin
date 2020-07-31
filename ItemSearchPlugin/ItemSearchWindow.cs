﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Data;
using Dalamud.Data.LuminaExtensions;
using Dalamud.Interface;
using Dalamud.Plugin;
using ImGuiNET;
using ImGuiScene;
using ItemSearchPlugin.ActionButtons;
using ItemSearchPlugin.Filters;
using Serilog;
using Lumina.Excel.GeneratedSheets;

namespace ItemSearchPlugin {
    internal class ItemSearchWindow : IDisposable {
        private readonly ItemSearchPlugin plugin;
        private readonly DalamudPluginInterface pluginInterface;
        private readonly DataManager data;
        private readonly UiBuilder builder;
        private Item selectedItem;
        private int selectedItemIndex = -1;
        private TextureWrap selectedItemTex;

        private CancellationTokenSource searchCancelTokenSource;
        private ValueTask<List<Item>> searchTask;

        private readonly ItemSearchPluginConfig pluginConfig;
        public List<SearchFilter> SearchFilters;
        public List<IActionButton> ActionButtons;

        private bool autoTryOn;
        private int debounceKeyPress;
        private bool doSearchScroll;
        private bool forceReload;

        private bool errorLoadingItems;

        public ItemSearchWindow(ItemSearchPlugin plugin, string searchText = "") {
            this.pluginInterface = plugin.PluginInterface;
            this.data = pluginInterface.Data;
            this.builder = pluginInterface.UiBuilder;
            this.pluginConfig = plugin.PluginConfig;
            this.plugin = plugin;

            while (!data.IsDataReady)
                Thread.Sleep(1);

            SearchFilters = new List<SearchFilter> {
                new ItemNameSearchFilter(pluginConfig, searchText),
                new ItemUICategorySearchFilter(pluginConfig, data),
                new LevelEquipSearchFilter(pluginConfig),
                new LevelItemSearchFilter(pluginConfig),
                new EquipAsSearchFilter(pluginConfig, data),
                new RaceSexSearchFilter(pluginConfig, data),
                new DyeableSearchFilter(pluginConfig),
                new StatSearchFilter(pluginConfig, data),
            };

            ActionButtons = new List<IActionButton> {
                new MarketBoardActionButton(pluginInterface, pluginConfig),
                new DataSiteActionButton(pluginConfig)
            };
        }

        private void UpdateItemList(int delay = 100) {
            errorLoadingItems = false;
            plugin.LuminaItems = null;
            plugin.LuminaItemsClientLanguage = pluginConfig.SelectedClientLanguage;
            Task.Run(async () => {
                await Task.Delay(delay);
                try {
                    return this.data.GetExcelSheet<Item>(pluginConfig.SelectedClientLanguage).ToList();
                } catch (Exception ex) {
                    errorLoadingItems = true;
                    PluginLog.LogError("Failed loading Items");
                    PluginLog.LogError(ex.ToString());
                    return new List<Item>();
                }
            }).ContinueWith(t => {
                if (errorLoadingItems) {
                    return plugin.LuminaItems;
                }

                forceReload = true;
                return plugin.LuminaItems = t.Result;
            });
        }

        public bool Draw() {
            var isSearch = false;
            if (pluginConfig.SelectedClientLanguage != plugin.LuminaItemsClientLanguage) UpdateItemList(1000);
            ImGui.SetNextWindowSize(new Vector2(500, 500), ImGuiCond.FirstUseEver);

            var isOpen = true;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(350, 400));

            if (!ImGui.Begin(Loc.Localize("ItemSearchPlguinMainWindowHeader", "Item Search") + "###itemSearchPluginMainWindow", ref isOpen, ImGuiWindowFlags.NoCollapse)) {
                ImGui.PopStyleVar();
                ImGui.End();
                return false;
            }

            ImGui.PopStyleVar();

            // Main window
            ImGui.AlignTextToFramePadding();

            if (this.selectedItemTex != null) {
                ImGui.SetCursorPosY(200f);
                ImGui.SameLine();
                ImGui.Image(this.selectedItemTex.ImGuiHandle, new Vector2(45, 45));

                if (selectedItem != null) {
                    ImGui.SameLine();
                    ImGui.BeginGroup();

                    ImGui.Text(selectedItem.Name);

                    if (pluginConfig.ShowItemID) {
                        ImGui.SameLine();
                        ImGui.Text($"(ID: {selectedItem.RowId})");
                    }

                    var imGuiStyle = ImGui.GetStyle();
                    var windowVisible = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;

                    IActionButton[] buttons = this.ActionButtons.Where(ab => ab.ButtonPosition == ActionButtonPosition.TOP).ToArray();

                    for (var i = 0; i < buttons.Length; i++) {
                        var button = buttons[i];

                        if (button.GetShowButton(selectedItem)) {
                            var buttonText = button.GetButtonText(selectedItem);
                            ImGui.PushID($"TopActionButton{i}");
                            if (ImGui.Button(buttonText)) {
                                button.OnButtonClicked(selectedItem);
                            }

                            if (i < buttons.Length - 1) {
                                var lX2 = ImGui.GetItemRectMax().X;
                                var nbw = ImGui.CalcTextSize(buttons[i + 1].GetButtonText(selectedItem)).X + imGuiStyle.ItemInnerSpacing.X * 2;
                                var nX2 = lX2 + (imGuiStyle.ItemSpacing.X * 2) + nbw;
                                if (nX2 < windowVisible) {
                                    ImGui.SameLine();
                                }
                            }

                            ImGui.PopID();
                        }
                    }

                    ImGui.EndGroup();
                }
            } else {
                ImGui.BeginChild("NoTextureBox", new Vector2(200, 45));
                ImGui.Text(Loc.Localize("ItemSearchSelectItem", "Please select an item."));
                ImGui.EndChild();
            }


            ImGui.Separator();

            ImGui.Columns(2);
            var filterNameMax = SearchFilters.Where(x => x.IsEnabled && x.ShowFilter).Select(x => {
                x._LocalizedName = Loc.Localize(x.NameLocalizationKey, x.Name);
                x._LocalizedNameWidth = ImGui.CalcTextSize($"{x._LocalizedName}").X;
                return x._LocalizedNameWidth;
            }).Max();

            ImGui.SetColumnWidth(0, filterNameMax + ImGui.GetStyle().ItemSpacing.X * 2);
            var filterInUseColour = new Vector4(0, 1, 0, 1);
            foreach (var filter in SearchFilters.Where(x => x.IsEnabled && x.ShowFilter)) {
                ImGui.SetCursorPosX((filterNameMax + ImGui.GetStyle().ItemSpacing.X) - filter._LocalizedNameWidth);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
                if (filter.IsSet) {
                    ImGui.TextColored(filterInUseColour, $"{filter._LocalizedName}: ");
                } else {
                    ImGui.Text($"{filter._LocalizedName}: ");
                }

                ImGui.NextColumn();
                filter.DrawEditor();
                while (ImGui.GetColumnIndex() != 0)
                    ImGui.NextColumn();
            }

            ImGui.Columns(1);
            var windowSize = ImGui.GetWindowSize();
            var childSize = new Vector2(0, Math.Max(100, windowSize.Y - ImGui.GetCursorPosY() - 40));
            ImGui.BeginChild("scrolling", childSize, true, ImGuiWindowFlags.HorizontalScrollbar);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

            if (errorLoadingItems) {
                ImGui.TextColored(new Vector4(1f, 0.1f, 0.1f, 1.00f), Loc.Localize("ItemSearchListLoadFailed", "Error loading item list."));
                if (ImGui.SmallButton("Retry")) {
                    UpdateItemList();
                }
            } else if (plugin.LuminaItems != null) {
                if (SearchFilters.Any(x => x.IsEnabled && x.ShowFilter && x.IsSet)) {
                    isSearch = true;
                    if (SearchFilters.Any(x => x.IsEnabled && x.ShowFilter && x.HasChanged) || forceReload) {
                        forceReload = false;
                        this.searchCancelTokenSource?.Cancel();
                        this.searchCancelTokenSource = new CancellationTokenSource();
                        var asyncEnum = plugin.LuminaItems.ToAsyncEnumerable();

                        if (!pluginConfig.ShowLegacyItems) {
                            asyncEnum = asyncEnum.Where(x => x.RowId < 100 || x.RowId > 1600);
                        }

                        asyncEnum = SearchFilters.Where(filter => filter.IsEnabled && filter.ShowFilter && filter.IsSet).Aggregate(asyncEnum, (current, filter) => current.Where(filter.CheckFilter));
                        this.selectedItemIndex = -1;
                        this.selectedItemTex?.Dispose();
                        this.selectedItemTex = null;
                        this.searchTask = asyncEnum.ToListAsync(this.searchCancelTokenSource.Token);
                    }

                    if (this.searchTask.IsCompletedSuccessfully) {
                        var itemSize = Vector2.Zero;
                        float cursorPosY = 0;
                        var scrollY = ImGui.GetScrollY();
                        var style = ImGui.GetStyle();
                        for (var i = 0; i < this.searchTask.Result.Count; i++) {
                            if (i == 0 && itemSize == Vector2.Zero) {
                                itemSize = ImGui.CalcTextSize(this.searchTask.Result[i].Name);
                                if (!doSearchScroll) {
                                    var sizePerItem = itemSize.Y + style.ItemSpacing.Y;
                                    var skipItems = (int) Math.Floor(scrollY / sizePerItem);
                                    cursorPosY = skipItems * sizePerItem;
                                    ImGui.SetCursorPosY(cursorPosY + style.ItemSpacing.X);
                                    i = skipItems;
                                }
                            }

                            if (!(doSearchScroll && selectedItemIndex == i) && (cursorPosY < scrollY - itemSize.Y || cursorPosY > scrollY + childSize.Y)) {
                                ImGui.SetCursorPosY(cursorPosY + itemSize.Y + style.ItemSpacing.Y);
                            } else if (ImGui.Selectable(this.searchTask.Result[i].Name, this.selectedItemIndex == i, ImGuiSelectableFlags.AllowDoubleClick)) {
                                this.selectedItem = this.searchTask.Result[i];
                                this.selectedItemIndex = i;

                                try {
                                    var iconTex = this.data.GetIcon(this.searchTask.Result[i].Icon);
                                    this.selectedItemTex?.Dispose();

                                    this.selectedItemTex =
                                        this.builder.LoadImageRaw(iconTex.GetRgbaImageData(), iconTex.Header.Width,
                                            iconTex.Header.Height, 4);
                                } catch (Exception ex) {
                                    Log.Error(ex, "Failed loading item texture");
                                    this.selectedItemTex?.Dispose();
                                    this.selectedItemTex = null;
                                }

                                if (ImGui.IsMouseDoubleClicked(0)) {
                                    if (this.selectedItemTex != null) {
                                        try {
                                            plugin.LinkItem(selectedItem);
                                            if (pluginConfig.CloseOnChoose) {
                                                this.selectedItemTex?.Dispose();
                                                isOpen = false;
                                            }
                                        } catch (Exception ex) {
                                            PluginLog.LogError(ex.ToString());
                                        }
                                    }
                                }

                                if ((autoTryOn = autoTryOn && pluginConfig.ShowTryOn) && plugin.FittingRoomUI.CanUseTryOn && pluginInterface.ClientState.LocalPlayer != null) {
                                    if (selectedItem.ClassJobCategory.Row != 0) {
                                        plugin.FittingRoomUI.TryOnItem(selectedItem);
                                    }
                                }
                            }

                            if (doSearchScroll && selectedItemIndex == i) {
                                doSearchScroll = false;
                                ImGui.SetScrollHereY(0.5f);
                            }

                            cursorPosY = ImGui.GetCursorPosY();

                            if (cursorPosY > scrollY + childSize.Y && !doSearchScroll) {
                                var c = this.searchTask.Result.Count - i;
                                ImGui.BeginChild("###scrollFillerBottom", new Vector2(0, c * (itemSize.Y + style.ItemSpacing.Y)), false);
                                ImGui.EndChild();
                                break;
                            }
                        }

                        var keyStateDown = ImGui.GetIO().KeysDown[0x28] || pluginInterface.ClientState.KeyState[0x28];
                        var keyStateUp = ImGui.GetIO().KeysDown[0x26] || pluginInterface.ClientState.KeyState[0x26];

                        var hotkeyUsed = false;
                        if (keyStateUp && !keyStateDown) {
                            if (debounceKeyPress == 0) {
                                debounceKeyPress = 5;
                                if (selectedItemIndex > 0) {
                                    hotkeyUsed = true;
                                    selectedItemIndex -= 1;
                                }
                            }
                        } else if (keyStateDown && !keyStateUp) {
                            if (debounceKeyPress == 0) {
                                debounceKeyPress = 5;
                                if (selectedItemIndex < searchTask.Result.Count - 1) {
                                    selectedItemIndex += 1;
                                    hotkeyUsed = true;
                                }
                            }
                        } else if (debounceKeyPress > 0) {
                            debounceKeyPress -= 1;
                            if (debounceKeyPress < 0) {
                                debounceKeyPress = 5;
                            }
                        }

                        if (hotkeyUsed) {
                            doSearchScroll = true;
                            this.selectedItem = this.searchTask.Result[selectedItemIndex];
                            try {
                                var iconTex = this.data.GetIcon(this.searchTask.Result[selectedItemIndex].Icon);
                                this.selectedItemTex?.Dispose();

                                this.selectedItemTex =
                                    this.builder.LoadImageRaw(iconTex.GetRgbaImageData(), iconTex.Header.Width,
                                        iconTex.Header.Height, 4);
                            } catch (Exception ex) {
                                Log.Error(ex, "Failed loading item texture");
                                this.selectedItemTex?.Dispose();
                                this.selectedItemTex = null;
                            }

                            if ((autoTryOn = autoTryOn && pluginConfig.ShowTryOn) && plugin.FittingRoomUI.CanUseTryOn && pluginInterface.ClientState.LocalPlayer != null) {
                                if (selectedItem.ClassJobCategory.Row != 0) {
                                    plugin.FittingRoomUI.TryOnItem(selectedItem);
                                }
                            }
                        }
                    }
                } else {
                    ImGui.TextColored(new Vector4(0.86f, 0.86f, 0.86f, 1.00f), Loc.Localize("DalamudItemSelectHint", "Type to start searching..."));

                    this.selectedItemIndex = -1;
                    this.selectedItemTex?.Dispose();
                    this.selectedItemTex = null;
                }
            } else {
                ImGui.TextColored(new Vector4(0.86f, 0.86f, 0.86f, 1.00f), Loc.Localize("DalamudItemSelectLoading", "Loading item list..."));
            }

            ImGui.PopStyleVar();

            ImGui.EndChild();

            // Darken choose button if it shouldn't be clickable
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, this.selectedItemIndex < 0 || this.selectedItemTex == null ? 0.25f : 1);

            if (ImGui.Button(Loc.Localize("Choose", "Choose"))) {
                try {
                    if (this.selectedItemTex != null) {
                        plugin.LinkItem(selectedItem);
                        if (pluginConfig.CloseOnChoose) {
                            this.selectedItemTex?.Dispose();
                            isOpen = false;
                        }
                    }
                } catch (Exception ex) {
                    Log.Error($"Exception in Choose: {ex.Message}");
                }
            }

            ImGui.PopStyleVar();

            if (!pluginConfig.CloseOnChoose) {
                ImGui.SameLine();
                if (ImGui.Button(Loc.Localize("Close", "Close"))) {
                    this.selectedItemTex?.Dispose();
                    isOpen = false;
                }
            }

            if (this.selectedItemIndex >= 0 && this.selectedItemTex == null) {
                ImGui.SameLine();
                ImGui.Text(Loc.Localize("DalamudItemNotLinkable", "This item is not linkable."));
            }

            if (pluginConfig.ShowTryOn && pluginInterface.ClientState.LocalPlayer != null) {
                ImGui.SameLine();
                ImGui.Checkbox(Loc.Localize("ItemSearchTryOnButton", "Try On"), ref autoTryOn);
            }

            string configText = Loc.Localize("ItemSearchConfigButton", "Config");
            if (isSearch) {
                string itemCountText = string.Format(Loc.Localize("ItemCount", "{0} Items"), this.searchTask.Result.Count);
                ImGui.SameLine(ImGui.GetWindowWidth() - (ImGui.CalcTextSize(configText).X + ImGui.GetStyle().ItemSpacing.X) - (ImGui.CalcTextSize(itemCountText).X + ImGui.GetStyle().ItemSpacing.X * 2));
                ImGui.Text(itemCountText);
            }

            ImGui.SameLine(ImGui.GetWindowWidth() - (ImGui.CalcTextSize(configText).X + ImGui.GetStyle().ItemSpacing.X * 2));
            if (ImGui.Button(configText)) {
                plugin.ToggleConfigWindow();
            }

            ImGui.End();

            return isOpen;
        }

        public void Dispose() {
            foreach (ISearchFilter f in SearchFilters) {
                f?.Dispose();
            }

            foreach (IActionButton b in ActionButtons) {
                b?.Dispose();
            }

            this.selectedItemTex?.Dispose();
        }
    }
}
