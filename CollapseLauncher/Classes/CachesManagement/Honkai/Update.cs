﻿using CollapseLauncher.Interfaces;
using Hi3Helper;
using Hi3Helper.Data;
using Hi3Helper.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static Hi3Helper.Locale;
using static Hi3Helper.Logger;

namespace CollapseLauncher
{
    internal partial class HonkaiCache
    {
        private async Task<bool> Update(List<CacheAsset> updateAssetIndex, List<CacheAsset> assetIndex)
        {
            // Assign Http client
            Http httpClient = new Http(true, 5, 1000, _userAgent);
            try
            {
                // Set IsProgressTotalIndetermined as false and update the status 
                _status.IsProgressTotalIndetermined = true;
                UpdateStatus();

                // Subscribe the event listener
                httpClient.DownloadProgress += _httpClient_UpdateAssetProgress;
                await Task.Run(() =>
                {
                    // Iterate the asset index and do update operation
                    foreach (CacheAsset asset in updateAssetIndex)
                    {
                        UpdateCacheAsset(asset, httpClient);
                    }
                });

                // Reindex the asset index in Verify.txt
                UpdateCacheVerifyList(assetIndex);

                return true;
            }
            catch (TaskCanceledException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogWriteLine($"An error occured while updating cache file!\r\n{ex}", LogType.Error, true);
                throw;
            }
            finally
            {
                // Unsubscribe the event listener and dispose Http client
                httpClient.DownloadProgress -= _httpClient_UpdateAssetProgress;
                httpClient.Dispose();
            }
        }

        private void UpdateCacheVerifyList(List<CacheAsset> assetIndex)
        {
            // Get listFile path
            string listFile = Path.Combine(_gamePath, "Data", "Verify.txt");

            // Initialize listFile File Stream
            using (FileStream fs = new FileStream(listFile, FileMode.Create, FileAccess.Write))
            using (StreamWriter sw = new StreamWriter(fs))
            {
                // Iterate asset index and generate the path for the cache path
                foreach (CacheAsset asset in assetIndex)
                {
                    // Yes, the path is written in this way. Idk why miHoYo did this...
                    string basePath = GetAssetBasePathByType(asset.DataType).Replace('\\', '/');
                    string path = basePath + "//" + asset.ConcatN;
                    sw.WriteLine(path);
                }
            }
        }

        private void UpdateCacheAsset(CacheAsset asset, Http httpClient)
        {
            // Increment total count and update the status
            _progressTotalCountCurrent++;
            _status.ActivityStatus = string.Format(Lang._Misc.Downloading + " {0}: {1}", asset.DataType, asset.N);
            UpdateAll();

            // This is a action for Unused asset.
            if (asset.DataType == CacheAssetType.Unused)
            {
                FileInfo fileInfo = new FileInfo(asset.ConcatPath);
                if (fileInfo.Exists)
                {
                    fileInfo.IsReadOnly = false;
                    fileInfo.Delete();
                }

                LogWriteLine($"Deleted unused file: {fileInfo.FullName}", LogType.Default, true);
            }
            // Other than unused file, do this action
            else
            {
                // Assign and check the path of the asset directory
                string assetDir = Path.GetDirectoryName(asset.ConcatPath);
                if (!Directory.Exists(assetDir))
                {
                    Directory.CreateDirectory(assetDir);
                }

                // Do multi-session download for asset that has applicable size
                if (asset.CS >= _sizeForMultiDownload)
                {
                    httpClient.DownloadSync(asset.ConcatURL, asset.ConcatPath, _downloadThreadCount, true, _token.Token);
                    httpClient.MergeSync();
                }
                // Do single-session download for others
                else
                {
                    httpClient.DownloadSync(asset.ConcatURL, asset.ConcatPath, true, null, null, _token.Token);
                }

                LogWriteLine($"Downloaded cache [T: {asset.DataType}]: {asset.N}", LogType.Default, true);
            }

            // Remove Asset Entry display
            Dispatch(() => AssetEntry.RemoveAt(0));
        }

        private void _httpClient_UpdateAssetProgress(object sender, DownloadEvent e)
        {
            // Only increase if the download state is not Mergin
            if (e.State != DownloadState.Merging)
            {
                _progressTotalSizeCurrent += e.Read;
            }

            // Update fetch status
            string timeLeftString = string.Format(Lang._Misc.TimeRemainHMSFormat, TimeSpan.FromSeconds((_progressTotalSize - _progressTotalSizeCurrent) / ConverterTool.Unzeroed((long)(e.Read / _stopwatch.Elapsed.TotalSeconds))));
            _status.ActivityTotal = string.Format(Lang._Misc.Downloading + ": {0}/{1} ", _progressTotalCountCurrent, _progressTotalCount)
                                       + string.Format($"({Lang._Misc.SpeedPerSec})", ConverterTool.SummarizeSizeSimple(e.Speed))
                                       + $" | {timeLeftString}";
            _status.IsProgressTotalIndetermined = false;

            // Update fetch progress=
            _progress.ProgressTotalPercentage = _progressTotalSizeCurrent != 0 ?
                ConverterTool.GetPercentageNumber(_progressTotalSizeCurrent, _progressTotalSize) :
                0;

            // Push status and progress update
            UpdateAll();
        }
    }
}