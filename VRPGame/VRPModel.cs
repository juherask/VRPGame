using Jypeli;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EdgeEndpoints = System.Collections.Generic.KeyValuePair<int, int>;

class VRPModel
{
    public static int DEPOT_IDX = 0;

    // These define the problem
    public List<Vector> points;
    public List<double> demands;
    public double capacity;
    public string problemName;
    public double BKS_val;
    public int BKS_k;
    public double[,] D;

    // The solution
    public List<List<GameObject>> neighbourEdges;
    public List<GameObject> solutionEdges;

    // Solution state datastructures
    public List<double> routeFillRatios;
    public List<bool> routeValidStates;
    public int[] routeIdxForNodes;
    public Dictionary<GameObject, int> routeIdxForEdges;

    public VRPModel()
    {
        points = new List<Vector>();
        demands = new List<double>();
        capacity = -1.0;
        problemName = "N/A";
        BKS_val = -1.0;
        BKS_k = -1;
        D = null;
    }
    #region Access model
    public int GetRouteIdxForEdge(GameObject edge)
    {
        if (routeIdxForEdges != null && edge!=null && routeIdxForEdges.ContainsKey(edge))
            return routeIdxForEdges[edge];
        return -1;
    }

    public int GetRouteIdxForNode(int nodeIdx)
    {
        if (routeIdxForNodes != null)
            return routeIdxForNodes[nodeIdx];
        return -1;
    }

    public IEnumerable<int> GetNodeNeighbours(int nodeIdx)
    {
        foreach (var edge in neighbourEdges[nodeIdx])
	    {
            var eps = (EdgeEndpoints)edge.Tag;
            if (eps.Key == nodeIdx)
                yield return eps.Value;
            else if (eps.Value == nodeIdx)
                yield return eps.Key;
            else
                throw new InvalidDataException("Internal data mismatch. The edge does not leave from the node it was attached to!");
	    }
    }
    public int GetNodeNeighbourCount(int nodeIdx)
    {
        return neighbourEdges[nodeIdx].Count;
    }
    #endregion

    #region Solution manipulations

    public bool ConnectNode(int customerIdx, List<GameObject> withEdges, out List<GameObject> removedEdges)
    {
        var fromIdxs = new List<int>();
        removedEdges = new List<GameObject>();
        foreach (var e in withEdges)
        {
            var eps = (EdgeEndpoints)(e.Tag);
            fromIdxs.Add(eps.Key);
        }

        if (customerIdx >= 0 && !fromIdxs.Contains(customerIdx))
        {
            for (int edgeIdx = 0; edgeIdx < withEdges.Count; edgeIdx++)
            {
                int fromIdx = fromIdxs[edgeIdx];

                bool notAneighbour = !GetNodeNeighbours(customerIdx).Contains(fromIdx);
                if (fromIdx != customerIdx && notAneighbour)
                {
                    int firstFrom = fromIdxs.First();

                    // if this customer is already routed
                    if (withEdges.Count == 1 &&
                        firstFrom != DEPOT_IDX &&
                        neighbourEdges[firstFrom].Count >= 2)
                    {
                        // Remove _oldest_
                        var rmEdge = RemoveExistingEdge(firstFrom, newest: false);
                        if (rmEdge != null) removedEdges.Add(rmEdge);
                    }
                    // if this customer is and we drag several edges with us
                    if (customerIdx != VRPModel.DEPOT_IDX)
                    {
                        List<int> nbListClone = new List<int>();
                        if (neighbourEdges[customerIdx].Count >= 2)
                        {
                            var rmEdge = RemoveExistingEdge(customerIdx, newest:true);
                            if (rmEdge != null) removedEdges.Add(rmEdge);
                        }
                    }


                    // Attach the edges to the customers
                    foreach (var edge in withEdges)
                    {
                        // snap to the point
                        var eps = (EdgeEndpoints)edge.Tag;
                        edge.Tag = new EdgeEndpoints(eps.Key, customerIdx);

                        AddEdgeToSolution(edge);
                    }

                    return true;
                }
            }
            return false;
        }
        else
        {
            return false;
        }
    }

    public GameObject RemoveExistingEdge(int leavingFromNodeId, bool newest)
    {

        int selectedIdx = -1;
        GameObject selectedEdge = null;
        if (!newest)
        {
            selectedIdx = solutionEdges.Count;
        }

        foreach (var edge in solutionEdges)
        {
            var eps = (EdgeEndpoints)(edge.Tag);
            if (eps.Key == leavingFromNodeId || eps.Value == leavingFromNodeId)
            {
                int seIdx = solutionEdges.IndexOf(edge);

                if ((newest && seIdx > selectedIdx) ||
                    (!newest && seIdx < selectedIdx))
                {
                    selectedIdx = seIdx;
                    selectedEdge = edge;
                }
            }
        }
        if (selectedEdge != null)
        {
            RemoveEdgeFromSolution(selectedEdge);
        }
        return selectedEdge;
    }

    public List<GameObject> RemoveEdges(int betweenThis, int andThat)
    {
        List<GameObject> toRemove = new List<GameObject>();
        foreach (var e in solutionEdges)
        {
            var eps = (EdgeEndpoints)e.Tag;
            // undirected
            if ((eps.Key == betweenThis && eps.Value == andThat) ||
                 (eps.Key == andThat && eps.Value == betweenThis))
            {
                toRemove.Add(e);
            }
        }
        foreach (var re in toRemove)
        {
            // let the RemoveEdge to update the internal bookkeeping
            RemoveEdgeFromSolution(re);
        }
        return toRemove; // remove these from the game
    }

    public void RemoveEdgeFromSolution(GameObject edge)
    {
        var eps = (EdgeEndpoints)edge.Tag;
        
        neighbourEdges[eps.Key].Remove(edge);
        neighbourEdges[eps.Value].Remove(edge);

        solutionEdges.Remove(edge);
    }

    public void AddEdgeToSolution(GameObject edge)
    {
        var eps = (EdgeEndpoints)edge.Tag;
        var fromNodeIdx = eps.Key;
        var toNodeIdx = eps.Value;

        solutionEdges.Add(edge);

        neighbourEdges[fromNodeIdx].Add(edge);
        neighbourEdges[toNodeIdx].Add(edge);

    }
    #endregion

    #region Feasibility and objective
    public  bool CheckFeasibility()
    {
        int N = points.Count;

        // Check that each node is visited (enters and leaves)
        bool allVisited = true;
        for (int i = 1; i < N; i++)
        {
            int nbrcount = 0;
            bool nbrToDepot = false;
            foreach (var edge in neighbourEdges[i])
            {
                var eps = (EdgeEndpoints)edge.Tag;
                if (eps.Key==DEPOT_IDX || eps.Value==DEPOT_IDX) nbrToDepot=true;
                nbrcount++;
            }

            if (!((nbrcount == 1 && nbrToDepot) ||
                nbrcount == 2))
            {
                allVisited = false;
                break;
            }
        }

        int reachableCount = 1;

        // While at it, check the capacity constraint, update route indexes and recod the fill ratio

        routeFillRatios = new List<double>();
        routeValidStates = new List<bool>();
        routeIdxForNodes = Enumerable.Repeat(-1, N).ToArray();
        routeIdxForEdges = new Dictionary<GameObject, int>();
        bool capacityConstraintViolated = false;

        int routeIdx = -1;
        List<int> depotReturnPoints = new List<int>();
        int firstAfterLeavingDepot = -1;
        foreach (var edgeFromDepot in neighbourEdges[DEPOT_IDX])
        {
            // Get idx of the next node from the depot
            var eps = (EdgeEndpoints)edgeFromDepot.Tag;
            firstAfterLeavingDepot = eps.Key;
            if (eps.Key == DEPOT_IDX) firstAfterLeavingDepot = eps.Value;

            if (depotReturnPoints.Contains(firstAfterLeavingDepot)) continue;
            routeIdx += 1;

            double routeCapacityRequirement = 0.0;
            int prevStop = DEPOT_IDX;
            int nextStop = firstAfterLeavingDepot;
            GameObject connectingEdge = edgeFromDepot;
            while (nextStop != DEPOT_IDX)
            {
                routeCapacityRequirement += demands[nextStop];

                if (routeIdxForNodes[nextStop] == -1)
                {
                    routeIdxForNodes[nextStop] = routeIdx;
                    reachableCount++;
                }
                routeIdxForEdges[connectingEdge] = routeIdx;

                bool foundNext = false;
                foreach (var nbrEdge in neighbourEdges[nextStop])
                {
                    var nbrEps = (EdgeEndpoints)nbrEdge.Tag;
                    int nbr = nbrEps.Key;
                    if (nbrEps.Key == nextStop) nbr = nbrEps.Value;

                    if (nbr != prevStop)
                    {
                        prevStop = nextStop;
                        nextStop = nbr;
                        connectingEdge = nbrEdge;
                        foundNext = true;
                        break;
                    }
                }
                if (!foundNext) break;
            }

            bool validRoute = false;
            if (prevStop == DEPOT_IDX || nextStop == DEPOT_IDX)
            {
                validRoute = true;
                depotReturnPoints.Add(prevStop);
            }

            routeValidStates.Add(validRoute);
            routeFillRatios.Add(routeCapacityRequirement);

            if (routeFillRatios[routeIdx] > capacity)
                capacityConstraintViolated = true;
        }

        if (!allVisited)
            return false;

        if (capacityConstraintViolated)
            return false;

        // There may be loops (not every customer is reachable from the depot)
        if (reachableCount < N)
            return false;


        return true;
    }

    public void CalculateObjectives(out int k, out double totd)
    {
        k = (int)Math.Ceiling(GetNodeNeighbourCount(DEPOT_IDX) / 2.0);
        totd = 0.0;
        List<EdgeEndpoints> epss = solutionEdges.Select(edge => (EdgeEndpoints)edge.Tag).ToList();
        foreach (var eps in epss)
        {
            totd += D[eps.Key, eps.Value];
        }
        // Check edges leaving for depot for back-forth to one point 
        //  (because of triangle inequality we do not have to check longer sequences)
        foreach (var edgeFromDepot in neighbourEdges[DEPOT_IDX])
        {
            // Get idx of the next node from the depot
            var eps = (EdgeEndpoints)edgeFromDepot.Tag;
            int firstNodeAfterLeavingDepot = eps.Key;
            if (eps.Key == DEPOT_IDX) firstNodeAfterLeavingDepot = eps.Value;

            //  has only depot as a neighbour, must come back.
            if (GetNodeNeighbourCount(firstNodeAfterLeavingDepot) == 1)
                totd += D[firstNodeAfterLeavingDepot, DEPOT_IDX];
        }
    }
    #endregion

    #region Load from TSP file
    enum TSPLibReaderState
    {
        Header,
        CoordSection,
        DemandSection,
        DepotSection,
        WeightSection
    };

    static public VRPModel LoadFromStream(StreamReader file, string problemName,
        double displayWidth,
        double displayHeight)
    {
        VRPModel model = new VRPModel();

        int N = 0;
        string weightStyle = "EUC_2D";
        string weightFormat = "FULL_MATRIX";
        string line;
        int xDIdx = 0; int yDIdx = 0; // indexes for EXPLICIT matrix wts
        TSPLibReaderState state = TSPLibReaderState.Header;
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
                model.D = new double[N, N];
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
                        model.capacity = Double.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    if (line.Contains("NAME"))
                    {
                        parts = line.Split(':');
                        model.problemName = parts[1].Trim();
                        model.BKS_k = Int32.Parse(problemName.Split('-').Last().Replace('k', ' '));
                    }
                    if (line.Contains("BEST_KNOWN"))
                    {
                        parts = line.Split(':');
                        model.BKS_val = Double.Parse(parts[1], CultureInfo.InvariantCulture);
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

                        if (weightFormat == "LOWER_ROW")
                            yDIdx = 1;
                        else if (weightFormat == "UPPER_ROW")
                            xDIdx = 1;
                    }
                    if (line.Contains("DIMENSION"))
                    {
                        parts = line.Split(':');
                        N = Int32.Parse(parts[1]);
                    }
                    break;
                case TSPLibReaderState.CoordSection:
                    parts = line.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (weightStyle == "GEO")
                    {
                        // id pos-x pos-y
                        model.points.Add(new Vector(
                             Double.Parse(parts[2], CultureInfo.InvariantCulture),
                             Double.Parse(parts[1], CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        // id pos-x pos-y
                        model.points.Add(new Vector(
                             Double.Parse(parts[1], CultureInfo.InvariantCulture),
                             Double.Parse(parts[2], CultureInfo.InvariantCulture)));
                    }

                    break;
                case TSPLibReaderState.DemandSection:
                    parts = line.Split(' ');
                    int id = Int32.Parse(parts[0]);
                    // one based indexing. first is the depot.
                    if (id == 1) model.demands.Add(model.capacity);
                    else model.demands.Add(Double.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                case TSPLibReaderState.WeightSection:
                    var distances = line.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dstr in distances)
                    {
                        if (dstr == "")
                        {
                            continue;
                        }

                        double d = Double.Parse(dstr, CultureInfo.InvariantCulture);
                        model.D[xDIdx, yDIdx] = d;
                        model.D[yDIdx, xDIdx] = d;

                        if (weightFormat == "LOWER_ROW")
                        {
                            xDIdx += 1;
                            if (xDIdx == yDIdx)
                            {
                                yDIdx++;
                                xDIdx = 0;
                            }
                        }
                        else if (weightFormat == "UPPER_ROW")
                        {
                            xDIdx++;
                            if (xDIdx == N)
                            {
                                yDIdx++;
                                xDIdx = yDIdx + 1;
                            }
                        }
                        else if (weightFormat == "FULL_MATRIX")
                        {
                            xDIdx++;
                            if (xDIdx == N)
                            {
                                yDIdx++;
                                xDIdx = 0;
                            }
                        }
                        else if (weightFormat == "LOWER_DIAG_ROW")
                        {
                            xDIdx++;
                            if (xDIdx == yDIdx + 1)
                            {
                                xDIdx = 0;
                                yDIdx++;
                            }
                        }
                        else if (weightFormat == "UPPER_DIAG_ROW")
                        {
                            xDIdx++;
                            if (xDIdx == N)
                            {
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
        if (model.D == null)
        {
            model.D = new double[model.points.Count, model.points.Count];
            for (int i = 0; i < model.points.Count; i++)
            {
                for (int j = i; j < model.points.Count; j++)
                {
                    if (i == j)
                    {
                        model.D[i, j] = 0.0;
                    }
                    else
                    {
                        double d = -1.0;
                        if (weightStyle == "EUC_2D")
                        {
                            d = Math.Round(Math.Sqrt( // rounded to nearest integer!
                              Math.Pow(model.points[i].X - model.points[j].X, 2) + // dx^2
                              Math.Pow(model.points[i].Y - model.points[j].Y, 2))); // dy^2
                        }
                        else if (weightStyle == "GEO")
                        {
                            /* from http://stackoverflow.com/questions/4913349/
                            Calculate the great circle distance between two points 
                            on the earth (specified in decimal degrees)
                            The distance should be within ~0.3% of the correct value.*/
                            // convert decimal degrees to radians 
                            double lon1 = (Math.PI / 180) * model.points[i].X;
                            double lat1 = (Math.PI / 180) * model.points[i].Y;
                            double lon2 = (Math.PI / 180) * model.points[j].X;
                            double lat2 = (Math.PI / 180) * model.points[j].Y;
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
                        model.D[i, j] = d;
                        model.D[j, i] = d;
                    }
                }
            }
        }


        // Scale to fill the screen
        double minX = model.points.Min((p) => p.X);
        double maxX = model.points.Max((p) => p.X);
        double minY = model.points.Min((p) => p.Y);
        double maxY = model.points.Max((p) => p.Y);

        double scale = Math.Min(displayWidth / (maxX - minX), displayHeight / (maxY - minY));
        double displayLeftOffset = (maxX - minX) * scale / 2;
        double displayTopOffset = (maxY - minY) * scale / 2;

        for (int i = 0; i < model.points.Count; i++)
        {
            Vector oldpt = model.points[i];
            Vector newpt = new Vector(
                (oldpt.X - minX) * scale - displayLeftOffset,
                (oldpt.Y - minY) * scale - displayTopOffset
            );
            model.points[i] = newpt;
        }

        // Init current solution data structures
        model.neighbourEdges = new List<List<GameObject>>();
        foreach (var p in model.points)
        {
            model.neighbourEdges.Add(new List<GameObject>());
        }
        model.solutionEdges = new List<GameObject>(model.points.Count);
        model.routeIdxForNodes = Enumerable.Repeat(-1, model.points.Count).ToArray();

        return model;
    }
    #endregion
}
