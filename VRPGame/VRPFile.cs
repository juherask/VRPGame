using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jypeli;
using System.IO;
using System.Globalization;


static class VRPFile
{
    enum TSPLibReaderState
    {
        Header,
        CoordSection,
        DemandSection,
        DepotSection,
        WeightSection
    };

    static public VRPModel LoadLevel(string fileName,
        double displayWidth,
        double displayHeight)
    {


        

        int N = 0;
        string weightStyle = "EUC_2D";
        string weightFormat = "FULL_MATRIX";
        string line;
        int xDIdx = 0; int yDIdx = 0; // indexes for EXPLICIT matrix wts
        TSPLibReaderState state = TSPLibReaderState.Header;
        System.IO.StreamReader file = new System.IO.StreamReader(fileName);
        while ((line = file.ReadLine()) != null)
        {
            // Check for keywords
            // TODO: Name, EDGE_WEIGHT_TYPE, CAPACITY

            if (line.Contains("NODE_COORD_SECTION") || line.Contains("DISPLAY_DATA_SECTION"))
            {
                state = TSPLibReaderState.CoordSection;
                continue;
            }
            else if (line.Contains("DEMAND_SECTION"))
            {
                state = TSPLibReaderState.DemandSection;
                continue;
            }
            else if (line.Contains("DEPOT_SECTION"))
            {
                state = TSPLibReaderState.DepotSection;
                continue;
            }
            else if (line.Contains("EDGE_WEIGHT_SECTION"))
            {
                D = new double[N, N];
                state = TSPLibReaderState.WeightSection;
                continue;
            }

            string[] parts;
            switch (state)
            {
                case TSPLibReaderState.Header:
                    if (line.Contains("CAPACITY"))
                    {
                        parts = line.Split(':');
                        capacity = Double.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    if (line.Contains("NAME"))
                    {
                        parts = line.Split(':');
                        problemName = parts[1].Trim();
                        string fileBaseName = Path.GetFileNameWithoutExtension(fileName);
                        k = Int32.Parse(fileBaseName.Split('-').Last().Replace('k', ' '));
                    }
                    if (line.Contains("BEST_KNOWN"))
                    {
                        parts = line.Split(':');
                        BKS = Double.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    if (line.Contains("EDGE_WEIGHT_TYPE"))
                    {
                        parts = line.Split(':');
                        weightStyle = parts[1].Trim();
                    }
                    if (line.Contains("EDGE_WEIGHT_FORMAT"))
                    {
                        parts = line.Split(':');
                        weightFormat = parts[1].Trim();

                        if (weightFormat=="LOWER_ROW")
                            yDIdx=1;
                        else if (weightFormat=="UPPER_ROW")
                            xDIdx=1;
                    }
                    if (line.Contains("DIMENSION"))
                    {
                        parts = line.Split(':');
                        N = Int32.Parse(parts[1]);
                    }
                    break;
                case TSPLibReaderState.CoordSection:
                    parts = line.Trim().Split(' ');
                    
                    if (weightStyle == "GEO")
                    {
                        // id pos-x pos-y
                        points.Add(new Vector(
                             Double.Parse(parts[2], CultureInfo.InvariantCulture),
                             Double.Parse(parts[1], CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        // id pos-x pos-y
                        points.Add(new Vector(
                             Double.Parse(parts[1], CultureInfo.InvariantCulture),
                             Double.Parse(parts[2], CultureInfo.InvariantCulture)));
                    }

                    break;
                case TSPLibReaderState.DemandSection:
                    parts = line.Split(' ');
                    int id = Int32.Parse(parts[0]);
                    // one based indexing. first is the depot.
                    if (id == 1) demands.Add(capacity);
                    else demands.Add(Double.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                case TSPLibReaderState.WeightSection:
                    var distances = line.Split(new string[]{" "}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dstr in distances)
	                {
                        if (dstr == "")
                        {
                            continue;
                        }

                        double d = Double.Parse(dstr, CultureInfo.InvariantCulture);
                        D[xDIdx,yDIdx] = d;
                        D[yDIdx,xDIdx] = d;
                            
                        if (weightFormat=="LOWER_ROW") {
                            xDIdx+=1;
                            if (xDIdx==yDIdx){
                                yDIdx++;
                                xDIdx = 0;
                            }                          
                        }
                        else if (weightFormat=="UPPER_ROW") {
                            xDIdx++;
                            if (xDIdx==N) {
                                yDIdx++;
                                xDIdx = yDIdx+1;                                   
                            }
                        }
                        else if (weightFormat=="FULL_MATRIX") {
                            xDIdx++;
                            if (xDIdx==N) {
                                yDIdx++;
                                xDIdx = 0;
                            }
                        }
                        else if (weightFormat=="LOWER_DIAG_ROW") {
                            xDIdx++;
                            if (xDIdx==yDIdx+1) {
                                xDIdx = 0;
                                yDIdx++;
                            }
                        }
                        else if (weightFormat=="UPPER_DIAG_ROW") {
                            xDIdx++;
                            if (xDIdx==N) {
                                yDIdx++;
                                xDIdx = yDIdx;
                            }
                        }
	                }
                    break;
                default:
                    break;
            }
        }

        // Calculate D (instance matrix) if needed
        if (D == null)
        {
            D = new double[points.Count, points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i; j < points.Count; j++)
                {
                    if (i == j)
                    {
                        D[i, j] = 0.0;
                    }
                    else
                    {
                        double d = -1.0;
                        if (weightStyle == "EUC_2D")
                        {
                            d = Math.Round(Math.Sqrt( // rounded to nearest integer!
                              Math.Pow(points[i].X - points[j].X, 2) + // dx^2
                              Math.Pow(points[i].Y - points[j].Y, 2))); // dy^2
                        }
                        else if (weightStyle == "GEO")
                        {
                            /* from http://stackoverflow.com/questions/4913349/
                            Calculate the great circle distance between two points 
                            on the earth (specified in decimal degrees)
                            The distance should be within ~0.3% of the correct value.*/
                            // convert decimal degrees to radians 
                            double lon1 = (Math.PI / 180) * points[i].X;
                            double lat1 = (Math.PI / 180) * points[i].Y;
                            double lon2 = (Math.PI / 180) * points[j].X;
                            double lat2 = (Math.PI / 180) * points[j].Y;
                            // haversine formula 
                            double dlon = lon2 - lon1;
                            double dlat = lat2 - lat1;
                            double a = Math.Pow(Math.Sin(dlat / 2), 2) +
                                       Math.Cos(lat1) * Math.Cos(lat2) *
                                       Math.Pow(Math.Sin(dlon / 2), 2);
                            double c = 2 * Math.Asin(Math.Sqrt(a));
                            double km = 6367 * c;
                            d = km;
                        }
                        D[i, j] = d;
                        D[j, i] = d;
                    }
                }
            }
        }


        // Scale to fill the screen
        double minX = points.Min((p) => p.X);
        double maxX = points.Max((p) => p.X);
        double minY = points.Min((p) => p.Y);
        double maxY = points.Max((p) => p.Y);

        double scale = Math.Min(displayWidth / (maxX - minX), displayHeight / (maxY - minY));
        double displayLeftOffset = (maxX - minX) * scale / 2;
        double displayTopOffset = (maxY - minY) * scale / 2;

        for (int i = 0; i < points.Count; i++)
        {
            Vector oldpt = points[i];
            Vector newpt = new Vector(
                (oldpt.X - minX) * scale - displayLeftOffset,
                (oldpt.Y - minY) * scale - displayTopOffset
            );
            points[i] = newpt;
        }

        // Init current solution data structures
        neighbourPoints = new List<List<int>>();
        foreach (var p in points)
        {
            neighbourPoints.Add(new List<int>());
        }
        solutionEdges = new List<GameObject>(points.Count);
        routeIdxForPoints = Enumerable.Repeat(-1, points.Count).ToArray();
    }
}
