﻿using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using SixLabors.Fonts;

namespace DEM.Net.Core.IO.SensorLog
{
    public static class SensorLogExtensions
    {
        private class SeriesDefinition : Dictionary<string, (Func<SensorLogData, float> getter, Predicate<float> noDatavalue)>
        {
        }
        public static void Plot(this SensorLog log, string outputFileName, int width = 1024, int height = 768)
        {
            SeriesDefinition seriesDefinition = new SeriesDefinition();
            seriesDefinition.Add("LocationLatitude", (getter: data => data.LocationLatitude, noDatavalue: v => v <= 0));
            seriesDefinition.Add("LocationLongitude", (getter: data => data.LocationLongitude, noDatavalue: v => v <= 0));
            seriesDefinition.Add("AltimeterRelativeAltitude", (getter: data => data.AltimeterRelativeAltitude, noDatavalue: v => v <= 0));
            seriesDefinition.Add("LocationAltitude", (getter: data => data.LocationAltitude, noDatavalue: v => v <= 0));
            seriesDefinition.Add("LocationMagneticHeading", (getter: data => data.LocationMagneticHeading, noDatavalue: v => v <= 0));
            seriesDefinition.Add("MotionYaw", (getter: data => data.MotionYaw, noDatavalue: v => v <= -9990));
            seriesDefinition.Add("MotionPitch", (getter: data => data.MotionPitch, noDatavalue: v => v <= -9990));
            seriesDefinition.Add("MotionRoll", (getter: data => data.MotionRoll, noDatavalue: v => v <= -9990));
            int margin = 10;
            //width = log.Data.Count + 2 * margin;
            List<PointF[]> dataseries = GetDataSeries(log, seriesDefinition);
            Color[] palette = { Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple, Color.Yellow, Color.Gray, Color.LimeGreen };
            var SeriesAndRanges = UpdateRanges(dataseries, seriesDefinition, width, height, margin);


            var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 12, FontStyle.Bold);
            float textSpacing = 3;

            const bool MERGED_IMAGE = false;
            if (MERGED_IMAGE)
            {
                using (Image<Rgba32> img = new Image<Rgba32>(width, height))
                {
                    img.Mutate(o =>
                            {
                                o.BackgroundColor(Color.White);
                                DrawAxis(o, width, height, margin);

                                int serieIndex = 0; int colorIndex = 0;
                                foreach (var serieDef in seriesDefinition)
                                {
                                    var serie = SeriesAndRanges.Series[serieIndex];
                                    Console.WriteLine($"Drawing serie {serieDef.Key}...");
                                    var pointsWithData = serie.Where(p => p.Y >= margin && p.Y <= (height - margin)).ToArray();
                                    o.DrawLines(palette[colorIndex], 1f, pointsWithData);


                                    serieIndex++;
                                    colorIndex = (colorIndex + 1) % palette.Length;
                                }

                                // Legend
                                serieIndex = 0; colorIndex = 0;
                                Console.WriteLine($"Drawing legend...");
                                foreach (var serieDef in seriesDefinition)
                                {
                                    var serie = SeriesAndRanges.Series[serieIndex];

                                    var text = $"{serieDef.Key} {SeriesAndRanges.Ranges[serieIndex].min:F2} to {SeriesAndRanges.Ranges[serieIndex].max:F2}";
                                    var textSize = TextMeasurer.Measure(text, new RendererOptions(font));
                                    var yText = margin + (textSize.Height + textSpacing) * serieIndex;
                                    o.DrawLines(palette[colorIndex], 2f, new PointF(margin * 2 + 2, yText), new PointF(margin * 2 + 45, yText));
                                    o.DrawText(text, font, Rgba32.Black, new PointF(margin * 2 + 50, yText));

                                    serieIndex++;
                                    colorIndex = (colorIndex + 1) % palette.Length;
                                }
                            }
                    );

                    img.Save(outputFileName);
                }
            }
            else
            {
                int serieIndex = 0; int colorIndex = 0;
                foreach (var serieDef in seriesDefinition)
                {
                    using (Image<Rgba32> img = new Image<Rgba32>(width, height))
                    {
                        img.Mutate(o =>
                        {
                            o.BackgroundColor(Color.White);
                            DrawAxis(o, width, height, margin);


                            var serie = SeriesAndRanges.Series[serieIndex];
                            Console.WriteLine($"Drawing serie {serieDef.Key}...");
                            var pointsWithData = serie.Where(p => p.Y >= margin && p.Y <= (height - margin)).ToArray();
                            o.DrawLines(palette[colorIndex], 1f, pointsWithData);


                            // Legend
                            Console.WriteLine($"Drawing legend...");

                            var text = $"{serieDef.Key} {SeriesAndRanges.Ranges[serieIndex].min:F2} to {SeriesAndRanges.Ranges[serieIndex].max:F2}";
                            var textSize = TextMeasurer.Measure(text, new RendererOptions(font));
                            var yText = margin + (textSize.Height + textSpacing);
                            o.DrawLines(palette[colorIndex], 2f, new PointF(margin * 2 + 2, yText), new PointF(margin * 2 + 45, yText));
                            o.DrawText(text, font, Rgba32.Black, new PointF(margin * 2 + 50, yText));
                        }
                        );

                        img.Save($"{serieDef.Key}_{outputFileName}");
                    }
                    serieIndex++;
                    colorIndex = (colorIndex + 1) % palette.Length;
                }
            }
        }


        private static List<PointF[]> GetDataSeries(SensorLog log, SeriesDefinition seriesDefinition)
        {
            List<PointF[]> series = new List<PointF[]>();
            int gettersCount = seriesDefinition.Count;
            foreach (var def in seriesDefinition)
            {
                var g = def.Value.getter;
                series.Add(new PointF[log.Count]);
            }

            for (int i = 0; i < log.Count; i++)
            {
                var data = log.Data[i];
                int defIndex = 0;
                foreach (var def in seriesDefinition)
                {
                    var getter = def.Value.getter;
                    series[defIndex][i] = new PointF(i, def.Value.getter(data));
                    defIndex++;
                }
            }
            return series;
        }
        private static (List<PointF[]> Series, List<(float min, float max)> Ranges) UpdateRanges(List<PointF[]> dataseries, SeriesDefinition seriesDefinition, int width, int height, int margin)
        {
            int dataCount = dataseries.First().Length;
            (float min, float max)[] ranges = ComputeRanges(dataseries, seriesDefinition.Select(kvp => kvp.Value.noDatavalue).ToList());
            var seriesKeys = seriesDefinition.Keys.ToArray();
            for (int i = 0; i < dataseries.Count; i++)
            {
                float MapY(float value) => MathHelper.Map(ranges[i].min, ranges[i].max, height - margin, margin, ranges[i].max - value + ranges[i].min, false);

                for (int j = 0; j < dataCount; j++)
                {
                    // Map index to xAxis
                    dataseries[i][j].X = MathHelper.Map(0, dataCount, margin, width - margin, dataseries[i][j].X, false);

                    // Map Y value to yAxis bounded
                    dataseries[i][j].Y = MapY(dataseries[i][j].Y);
                }
            }
            return (dataseries, ranges.ToList());
        }
        private static (float min, float max)[] ComputeRanges(List<PointF[]> dataseries, List<Predicate<float>> noDataValues)
        {
            if (noDataValues == null)
            {
                return dataseries.Select((data, index) => (
                    data.Min(d => d.Y)
                    , data.Max(d => d.Y)
                )).ToArray();
            }
            else
            {
                return dataseries.Select((data, index) => (
                    data.Where(d => !noDataValues[index](d.Y)).Min(d => d.Y)
                    , data.Where(d => !noDataValues[index](d.Y)).Max(d => d.Y)
                )).ToArray();
            }
        }


        private static void DrawAxis(IImageProcessingContext ctx, int width, int height, int margin)
        {
            PointF[] xAxis = { new PointF(margin, height - margin), new PointF(width - margin, height - margin) };
            PointF[] yAxis = { new PointF(margin, margin), new PointF(margin, height - margin) };

            ctx.DrawLines(Color.Black, 1, xAxis)
               .DrawLines(Color.Black, 1, yAxis);
        }
    }
}
