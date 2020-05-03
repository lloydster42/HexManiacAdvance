﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace HavenSoft.HexManiac.Core.Models.Runs.Sprites {
   public class LzPaletteRun : LZRun, IPaletteRun {
      public PaletteFormat PaletteFormat { get; }

      public override string FormatString { get; }

      public int Pages {
         get {
            var length = Model.ReadMultiByteValue(Start + 1, 3);
            var paletteLength = (int)Math.Pow(2, PaletteFormat.Bits) * 2;
            return length / paletteLength;
         }
      }

      public LzPaletteRun(PaletteFormat paletteFormat, IDataModel data, int start, IReadOnlyList<int> sources = null)
         : base(data, start, sources) {
         PaletteFormat = paletteFormat;
         if ((int)Math.Pow(2, paletteFormat.Bits) * 2 > DecompressedLength) InvalidateLength();
         FormatString = $"`lzp{paletteFormat.Bits}`";
      }

      public static bool TryParsePaletteFormat(string pointerFormat, out PaletteFormat paletteFormat) {
         paletteFormat = default;
         if (!pointerFormat.StartsWith("`lzp") || !pointerFormat.EndsWith("`")) return false;
         return TryParseDimensions(pointerFormat, out paletteFormat);
      }

      public static bool TryParseDimensions(string format, out PaletteFormat paletteFormat) {
         paletteFormat = default;
         var formatContent = format.Substring(4, format.Length - 5);
         var pageSplit = formatContent.Split(':');
         int pages = 1, pageStart = 0;
         if (pageSplit.Length == 2) {
            var lastPageID = pageSplit[1].ToUpper().LastOrDefault();
            var lastPageIndex = ViewModels.ViewPort.AllHexCharacters.IndexOf(lastPageID);
            if (lastPageIndex > 0) pages = lastPageIndex + 1;
            var firstPageID = pageSplit[1].ToUpper().FirstOrDefault();
            var firstPageIndex = ViewModels.ViewPort.AllHexCharacters.IndexOf(firstPageID);
            if (firstPageIndex > 0) pageStart = firstPageIndex;
            pages -= firstPageIndex;
         }

         if (!int.TryParse(pageSplit[0], out var bits)) return false;
         paletteFormat = new PaletteFormat(bits, pages, pageStart);
         return true;
      }

      protected override BaseRun Clone(IReadOnlyList<int> newPointerSources) => new LzPaletteRun(PaletteFormat, Model, Start, newPointerSources);

      public IReadOnlyList<short> GetPalette(IDataModel model, int page) {
         var data = Decompress(model, Start);
         var colorCount = (int)Math.Pow(2, PaletteFormat.Bits);
         var pageLength = colorCount * 2;
         page %= Pages;
         return PaletteRun.GetPalette(data, page * pageLength, colorCount);
      }

      public IPaletteRun SetPalette(IDataModel model, ModelDelta token, int page, IReadOnlyList<short> colors) {
         var data = Decompress(model, Start);
         var colorCount = (int)Math.Pow(2, PaletteFormat.Bits);
         var pageLength = colorCount * 2;
         page %= Pages;
         PaletteRun.SetPalette(data, page * pageLength, colors);

         var newModelData = Compress(data, 0, data.Length);
         var newRun = (IPaletteRun)model.RelocateForExpansion(token, this, newModelData.Count);
         for (int i = 0; i < newModelData.Count; i++) token.ChangeData(model, newRun.Start + i, newModelData[i]);
         for (int i = newModelData.Count; i < Length; i++) token.ChangeData(model, newRun.Start + i, 0xFF);
         newRun = new LzPaletteRun(PaletteFormat, model, newRun.Start, newRun.PointerSources);
         model.ObserveRunWritten(token, newRun);
         return newRun;
      }
   }
}