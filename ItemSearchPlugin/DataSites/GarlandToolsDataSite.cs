﻿using Lumina.Excel.GeneratedSheets;
using Item = ItemSearchPlugin.ItemTemp;
namespace ItemSearchPlugin.DataSites {
    public class GarlandToolsDataSite : DataSite {
        public override string Name => "Garland Tools";

        public override string NameTranslationKey => "GarlandToolsDataSite";

        public override string GetItemUrl(Item item) => $"https://www.garlandtools.org/db/#item/{item.RowId}";
    }
}
