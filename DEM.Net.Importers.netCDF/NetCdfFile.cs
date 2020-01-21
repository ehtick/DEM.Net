﻿using DEM.Net.Core;
using Microsoft.Research.Science.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DEM.Net.Importers.netCDF
{
    /// <summary>
    /// https://www.gebco.net/data_and_products/gridded_bathymetry_data/gebco_2019/gebco_2019_info.html
    /// The netCDF storage is arranged as contiguous latitudinal bands.
    /// </summary>
    public class NetCdfFile : IRasterFile
    {
        private readonly string _filename;
        private DataSet _dataset;
        private const string LAT = "lat";
        private const string LONG = "lon";
        private const string ELEV = "elevation";

        private Variable _latVariable;
        private Variable _longVariable;
        private Variable _elevationVariable;

        #region Lifecycle
        public NetCdfFile(string fileName)
        {
            _filename = fileName;

            OpenDataset();
        }
        private void OpenDataset()
        {
            try
            {
                _dataset = DataSet.Open(_filename, ResourceOpenMode.ReadOnly);
                var varNamesAndTypes = _dataset.Variables.ToDictionary(v => v.Name, v => v.TypeOfData);

                void CheckVariable<T>(string varName)
                {
                    if (!varNamesAndTypes.ContainsKey(varName))
                    {
                        throw new KeyNotFoundException($"NetCDF file must contain ${varName} variable.");
                    }
                    else if (!varNamesAndTypes[varName].Equals(typeof(T)))
                    {
                        throw new InvalidCastException($"NetCDF variable {varName} is of type {varNamesAndTypes[varName].Name} and doesn't match excpected type {typeof(T).Name}.");
                    }
                }

                CheckVariable<double>(LAT);
                CheckVariable<double>(LONG);
                CheckVariable<float>(ELEV);

                _latVariable = _dataset.Variables[LAT];
                _longVariable = _dataset.Variables[LONG];
                _elevationVariable = _dataset.Variables[ELEV];
            }
            catch (Exception ex)
            {
                throw new FileLoadException($"{nameof(NetCdfFile)}: Cannot open file {_filename}. Check if file exists and if netCdf binaries are installed on your system. See https://www.unidata.ucar.edu/software/netcdf/docs/winbin.html for installation instructions, or the repo ReadMe here: https://github.com/predictionmachines/SDSlite."
                    , _filename, ex);
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // Pour détecter les appels redondants
        

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _dataset?.Dispose();
                    _dataset = null;
                }

                disposedValue = true;
            }
        }

        // Ce code est ajouté pour implémenter correctement le modèle supprimable.
        public void Dispose()
        {
            Dispose(true);
        }


        #endregion
        #endregion

        public float GetElevationAtPoint(FileMetadata metadata, int x, int y)
        {
            throw new NotImplementedException();
        }

        public HeightMap GetHeightMap(FileMetadata metadata)
        {
            HeightMap heightMap = new HeightMap(metadata.Width, metadata.Height);
            heightMap.Count = heightMap.Width * heightMap.Height;
            var coords = new List<GeoPoint>(heightMap.Count);

            
            MultipleDataResponse response = _dataset.GetMultipleData(
                DataRequest.GetData(_elevationVariable),
                DataRequest.GetData(_latVariable),
                DataRequest.GetData(_longVariable));

            Array latitudes = response[_latVariable.ID].Data;
            Array longitudes = response[_longVariable.ID].Data;
            Array elevations = response[_elevationVariable.ID].Data;

            int index = 0;

            var elevationsEnumerator = elevations.GetEnumerator();
            foreach (double longitude in longitudes) 
            {
                foreach (double latitude in latitudes)
                {
                    elevationsEnumerator.MoveNext();
                    float heightValue = (float)elevationsEnumerator.Current;

                    coords.Add(new GeoPoint(latitude, longitude, heightValue));

                    index++;
                }
            }

            heightMap.Coordinates = coords;
            return heightMap;
        }

        public HeightMap GetHeightMapInBBox(BoundingBox bbox, FileMetadata metadata, float noDataValue = float.MinValue)
        {
            throw new NotImplementedException();
        }

        public FileMetadata ParseMetaData(DEMFileDefinition fileFormat)
        {
            // Data origin is lower left corner
            try
            {
                int[] shape = new int[1];
                shape[0] = 2;
                int ncols = _longVariable.Dimensions.First().Length;
                int nrows = _latVariable.Dimensions.First().Length;

                Array longValues = _dataset.Variables[LONG].GetData(null, shape); 
                Array latValues = _dataset.Variables[LAT].GetData(null, shape);
                double xllcorner = (double)longValues.GetValue(0);
                double yllcorner = (double)latValues.GetValue(0);
                double cellsizex = (double)longValues.GetValue(1)- (double)longValues.GetValue(0);
                double cellsizey = (double)latValues.GetValue(1) - (double)latValues.GetValue(0);
                float NODATA_value = -9999f;

                FileMetadata metadata = new FileMetadata(_filename, fileFormat);
                metadata.Height = nrows;
                metadata.Width = ncols;
                metadata.PixelScaleX = cellsizex;
                metadata.PixelScaleY = cellsizey;
                metadata.pixelSizeX = metadata.PixelScaleX;
                metadata.pixelSizeY = metadata.PixelScaleY;

                if (fileFormat.Registration == DEMFileRegistrationMode.Grid)
                {
                    metadata.DataStartLat = yllcorner;
                    metadata.DataStartLon = xllcorner;
                    metadata.DataEndLat = yllcorner + metadata.Height * metadata.pixelSizeY;
                    metadata.DataEndLon = xllcorner + metadata.Width * metadata.pixelSizeX;

                    metadata.PhysicalStartLat = yllcorner;
                    metadata.PhysicalStartLon = xllcorner;
                    metadata.PhysicalEndLat = metadata.DataEndLat;
                    metadata.PhysicalEndLon = metadata.DataEndLon;
                }
                else
                {
                    metadata.DataStartLat = Math.Round(yllcorner + (metadata.PixelScaleY / 2.0), 10);
                    metadata.DataStartLon = Math.Round(xllcorner + (metadata.PixelScaleX / 2.0), 10);
                    metadata.DataEndLat = Math.Round(metadata.DataEndLat - (metadata.PixelScaleY / 2.0), 10);
                    metadata.DataEndLon = Math.Round(metadata.DataEndLon - (metadata.PixelScaleX / 2.0), 10);

                    metadata.PhysicalStartLat = metadata.DataStartLat;
                    metadata.PhysicalStartLon = metadata.DataStartLon;
                    metadata.DataEndLat = yllcorner + metadata.Height * metadata.pixelSizeY;
                    metadata.DataEndLon = xllcorner + metadata.Width * metadata.pixelSizeX;
                }

                metadata.SampleFormat = RasterSampleFormat.FLOATING_POINT;
                metadata.NoDataValue = NODATA_value.ToString();
                return metadata;

            }
            catch (Exception)
            {
                throw;
            }
        }

        public string GetMetadataReport()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"=================== Dataset {_dataset.Name}");
            sb.AppendLine("Metadata:");
            foreach (var data in _dataset.Metadata)
            {
                sb.AppendLine($"{data.Key}:");
                sb.AppendLine($"{data.Value}");
            }
            sb.AppendLine($"=================== Variables ");
            foreach (var v in _dataset.Variables)
            {
                sb.AppendLine($"{v.Name}:");
                sb.AppendLine(v.ToString());
            }

            return sb.ToString();
        }
    }
}
