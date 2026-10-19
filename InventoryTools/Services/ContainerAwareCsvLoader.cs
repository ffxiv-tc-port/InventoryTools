using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AllaganLib.GameSheets.Sheets;
using Autofac;
using CriticalCommonLib.Helpers;
using CriticalCommonLib.Models;
using CSVFile;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using Sylvan.Data.Csv;

namespace InventoryTools.Services;

public class ContainerAwareCsvLoader
{
    private readonly IComponentContext _componentContext;
    private readonly GameData _gameData;

    public ContainerAwareCsvLoader(IComponentContext componentContext, GameData gameData)
    {
        _componentContext = componentContext;
        _gameData = gameData;
    }

    public bool ToCsvRaw<T>(List<T> items, string filePath) where T : ICsv
    {
        try
        {
            using (StreamWriter dest = new StreamWriter(filePath))
            {
                CSVWriter csvWriter = new CSVWriter(dest);
                foreach (T obj in items)
                {
                    if (obj.IncludeInCsv())
                        csvWriter.WriteLine((IEnumerable<object>) obj.ToCsv());
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    /// <summary>
    /// 輸出內容與 <see cref="ToCsvRaw{T}"/> 相同，但先寫同目錄的 <c>.tmp</c>、落盤後才取代正式檔。
    /// 成功回 null；任何一步失敗回例外，暫存檔刪掉、正式檔保持原內容
    /// （正式檔正被別人開著時取代會失敗，屬於這一類；對方帶不帶 FileShare.Delete 都一樣）。
    /// </summary>
    public static Exception? ToCsvRawAtomic<T>(List<T> items, string filePath) where T : ICsv
    {
        return CsvRowWriter.ToCsvRawAtomic(items, filePath);
    }

    public List< T > LoadCsv<T>(string filePath, out List<string> failedLines, out List<Exception> exceptions) where T : ICsv
    {
        return LoadCsv(filePath, () => _componentContext.Resolve<T>(), out failedLines, out exceptions);
    }

    /// <summary>
    /// 與 <c>LoadCsv&lt;InventoryItem&gt;</c> 相同，但每列直接 new：容器裡的 InventoryItem 是 transient、只有這一個建構子、
    /// 不是 IDisposable，兩張表都是同一個快取物件；讀到第一列時才解析（與逐列解析的時機相同）。
    /// </summary>
    public List<InventoryItem> LoadInventoryItemsCsv(string filePath, out List<string> failedLines, out List<Exception> exceptions)
    {
        ItemSheet? itemSheet = null;
        ExcelSheet<Stain>? stainSheet = null;
        return LoadCsv(filePath, () => new InventoryItem(
            itemSheet ??= _componentContext.Resolve<ItemSheet>(),
            stainSheet ??= _componentContext.Resolve<ExcelSheet<Stain>>()), out failedLines, out exceptions);
    }

    private List< T > LoadCsv<T>(string filePath, Func<T> create, out List<string> failedLines, out List<Exception> exceptions) where T : ICsv
    {
        failedLines = new List< string >();
        exceptions = new List< Exception >();
        var items = new List< T >();

        using CsvDataReader dr = CsvDataReader.Create(filePath, new CsvDataReaderOptions(){HasHeaders = false});
        while (dr.Read())
        {
            string[] fields = new string[dr.FieldCount];

            for(int i = 0; i < dr.FieldCount; i++)
            {
                fields[i] = dr.GetString(i);
            }
            T item = create();
            try
            {
                item.FromCsv( fields );
                if( _gameData != null )
                {
                    item.PopulateData( _gameData.Excel, _gameData.Options.DefaultExcelLanguage );
                }
                items.Add( item );
            }
            catch( Exception e )
            {
                exceptions.Add(e);
                failedLines.Add( String.Join( ",",fields ) );
            }
        }
        return items;
    }
}