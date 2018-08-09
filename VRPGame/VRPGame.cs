﻿using System;
using System.Collections.Generic;
using Jypeli;
using Jypeli.Assets;
using Jypeli.Controls;
using Jypeli.Effects;
using Jypeli.Widgets;
using System.Linq;
using System.IO;
using System.IO.Compression;

// TODO:
// - buttons to : back to menu. more info.
// - Play against C64 etc https://en.wikipedia.org/wiki/Instructions_per_second
// Ideas:
// - by playing you can get coins to use: or-opt, 2-opt etc.
//
// - OR show the best-known sol (optionally step by step)
// - Option to show capacities
// - All routes with different colors incl. result bar (also capacity, also exceeded capacity)
// x Exsting routes are fixed until one removes a node from a route with a click
// - Remove non-eucudian cases
// After that:
// - VRPTW cases
// - Map layout background and routes (optionally) follow streets
// - Solution from NFLEET solver

using EdgeEndpoints = System.Collections.Generic.KeyValuePair<int, int>;

public class VRPGame : Game
{
    static double DOT_MAX_SIZE_RATIO = 20.0;
    static double DOT_MIN_SIZE_PX = 32.0;
    static double DOT_MIN_SIZE_MAX_ALLOWED_OVERLAP = 1.5;
    static double BORDER_WIDTH = 1.5;
    static double EDGE_WIDTH = 4.0;
    static double EDGE_HIT_WIDHT = 8.0;
    static public double SCREEN_MARGIN_WIDHT_RATIO = 5.0; // e.g. 1/5th (1/10th each side)
    static public double TOP_MARGIN = 50;
    static public double TRUCK_BAR_HEIGHT = 10;
    static string[] HELPTEXT = new string[]{
        "KULJETUSTEN OPTIMOINTIPELI",
        "",
        "Reititä kaikki harmaat pisteet mahdollisimman vähäi-",
        "sellä määrällä reittejä ja lyhyellä kokonaismatkalla.",
        "",
        "Vedä sormea pitkin näyttöä niin peli lisää siirtymiä",
        "pisteiden välille. Kaikkien reittien pitää alkaa ja",
        "päättyä varikolle, eli punaiseen pisteeseen. Pisteen",
        "koko kertoo kysynnän , eli kuinka paljon kuljetettavaa",
        "tuotetta pisteeseen pitää viedä. Jos reitin pisteet",
        "muuttuvat oransseiksi, olet ylittänyt kantokyvyn ja",
        "reitti pitää miettiä uudelleen. Voit poistaa siirty-",
        "miä tökkäämällä pistettä tai siirtymää.",
        "",
        "Ratkomalla tehtäviä, saat auki uusia vaikeampia teh-",
        "täviä. Ja kolme tähteä saa vain hyvällä reitityksellä.",
        ""
    };


    static public Color BackgroundColor = Color.FromHexCode("F7F5ED");
    static public Color NotRoutedColor = Color.FromHexCode("7B8D8E");
    static public Color BorderColor = Color.FromHexCode("5D4C46");
    static public Color DepotColor = Color.FromHexCode("E45641");
    static public Color InfeasibleColor = Color.FromHexCode("F1A94E");
    static public Color RoutedColor = Color.FromHexCode("2E7882");
    static public Color CompleteRouteColor = Color.FromHexCode("44B3C2");
    
    // The problems
    List<VRPProblemSelector.ProblemInstance> problemInstanceMetadata = null;
    VRPProblemSelector.ProblemInstance currentInstanceMetadata = null;
    VRPModel instance = null;

    // UI and counters
    List<ProgressBar> truckFillBars = new List<ProgressBar>();
    VRPProblemSelector problemMenu = null;
    IntMeter trucksUsed;
    DoubleMeter distanceTraveled;
    IntMeter ofBKS;
    Label trucksDisplay;
    Label distanceDisplay;
    Label ofBKSDisplay;
    Label ofBKSDisplayNA;
    List<Label> nodeLabels = null;

    // This is for live edit state
    int mouseDownPointIdx = -1;
    GameObject mouseDownEdge = null;
    int activePointIdx = -1;
    GameObject activePointObject = null;
    List<GameObject> dragEdges = new List<GameObject>();
    Dictionary<Vector, GameObject> pointToCircleObject = new Dictionary<Vector, GameObject>();
    
    // helpers
    double dotMaxSize = 0.0;

    public override void Begin()
    {
        TouchPanel.Listen(ButtonState.Down, (tp) => MessageDisplay.Add("liikkuu"), "Liikuttaa pelaajaa");

        //SetWindowSize(1050, 590);
        // Contols and events
        Level.Background.Color = BackgroundColor;
        Mouse.IsCursorVisible = true;
        PhoneBackButton.Listen(ConfirmExit, "Lopeta peli");
        Keyboard.Listen(Key.Escape, ButtonState.Pressed, ConfirmExit, "Lopeta peli");
        Keyboard.Listen(Key.L, ButtonState.Released, ToggleNodeLables, "Näytä numerointi");
        //Keyboard.Listen(Key.D, ButtonState.Released, ToggleNodeDemands, "Näytä tilausten koot");

        // Select the problem to play
        problemMenu = new VRPProblemSelector(this);
        problemInstanceMetadata = VRPProblemSelector.LoadInstanceDataFromFile(@"Content\problemdata.txt");
        VRPProblemSelector.LoadPlayerInstanceDataFromFile(@"Content\playerdata.txt", problemInstanceMetadata);

        //Mouse.Listen(MouseButton.Left, ButtonState.Pressed, MenuMousePressed, "Valitse kenttä");
        problemMenu.ViewSetSelect(problemInstanceMetadata, OnSetSelected);

        //StartGame(@"C:\Users\juherask\Dissertation\Phases\Tuning\Benchmarks\VRPLIB\E\E-n13-k4.vrp");
    }

    void ChooseNextLevel()
    {
        // Reset everything
        ClearGameObjects();
        Mouse.Clear();
        currentInstanceMetadata = null;
        instance = null;
        truckFillBars = new List<ProgressBar>();
        mouseDownPointIdx = -1;
        mouseDownEdge = null;
        activePointIdx = -1;
        activePointObject = null;
        dragEdges = new List<GameObject>();
        pointToCircleObject = new Dictionary<Vector, GameObject>();

        //Mouse.Listen(MouseButton.Left, ButtonState.Pressed, MenuMousePressed, "Valitse kenttä");
        //TouchPanel.Listen(ButtonState.Released, OnTouchEvent, "Valitse kenttä");
        problemMenu.ViewSetSelect(problemInstanceMetadata, OnSetSelected);
    }

    void OnSetSelected(string set)
    {
        var setInstances = problemInstanceMetadata.Where(pi => pi.ProblemSet == set).ToList();
        // complex mathz that maps 27->7, 23&24->6, 13->6, 5->3
        //  in plain math: ceil(log(x)^2/2+1) or 3, whichever is bigger
        int perRow = Math.Max(3,(int)Math.Ceiling( Math.Pow(Math.Log(setInstances.Count),2)/2.0)+1);
        //MessageDisplay.Add(perRow.ToString());
        problemMenu.ViewProblemSelect(setInstances, OnInstanceSelected, perRow);
    }

    void OnInstanceSelected(VRPProblemSelector.ProblemInstance instance)
    {
        currentInstanceMetadata = instance;
        StartGame(instance.ProblemFile);
    }

    void ShowHelp()
    {
        Widget helpWindow = new Widget(new VerticalLayout());
        helpWindow.Layout.LeftPadding = 20;
        helpWindow.Layout.RightPadding = 20;
        helpWindow.Layout.TopPadding = 20;
        helpWindow.Layout.BottomPadding = 20;
        foreach (var line in HELPTEXT)
        {
            Label linelabel = new Label(line);
            helpWindow.Add(linelabel);
        }
        PushButton closeHelpBtn = new PushButton("Sulje");
        closeHelpBtn.Clicked += () => Remove(helpWindow);
        helpWindow.Add(closeHelpBtn);
        Add(helpWindow,-1);
    }

    public void StartGame(string problemFilePath)
    {
        Mouse.Listen(MouseButton.Left, ButtonState.Pressed, MouseBtnGoesDown, "Tökkää piirtääksesi reittejä");
        Mouse.Listen(MouseButton.Left, ButtonState.Released, MouseBtnGoesUp, "");
        Mouse.ListenMovement(0.1, MouseMoves, "Rakenna reittejä venyttämällä");

        
        // UI
        trucksUsed = new IntMeter(0);
        trucksDisplay = CreateMeterDisplay(
            trucksUsed,
            new Vector(-Screen.Width / 4, Screen.Top - TOP_MARGIN),
            LoadImage("truck"));

        distanceTraveled = new DoubleMeter(0.0);
        distanceDisplay = CreateMeterDisplay(
            distanceTraveled,
            new Vector(0, Screen.Top - TOP_MARGIN),
            LoadImage("road"));

        ofBKS = new IntMeter(99);
        ofBKSDisplay = CreateMeterDisplay(
            ofBKS,
            new Vector(Screen.Width / 4, Screen.Top - TOP_MARGIN),
            LoadImage("dash"));
        ofBKSDisplay.IntFormatString = "{0} %";
        UpdateDisplayIconPosition(ofBKSDisplay);
        ofBKSDisplay.IsVisible = false;
        // use "--" label until feasible
        ofBKSDisplayNA = new Label("-- %");
        ofBKSDisplayNA.Position = ofBKSDisplay.Position;
        Add(ofBKSDisplayNA, 3);

        // Navigation buttons


        PushButton backButton = new PushButton(ofBKSDisplay.Width * 1.1, ofBKSDisplay.Height * 1.0, LoadImage("back_btn"));
        backButton.Clicked += () => ChooseNextLevel();
        backButton.Position = new Vector( -Screen.Width/2+ofBKSDisplay.Height*1.5, ofBKSDisplay.Position.Y);
        Add(backButton);

        PushButton helpButton = new PushButton(ofBKSDisplay.Width * 1.1, ofBKSDisplay.Height * 1.0, LoadImage("help_btn"));
        helpButton.Clicked += () => ShowHelp();
        helpButton.Position = new Vector(Screen.Width / 2 - ofBKSDisplay.Height * 1.5, ofBKSDisplay.Position.Y);
        Add(helpButton);

        string problemName = Path.GetFileNameWithoutExtension(problemFilePath);
        if (File.Exists(problemFilePath))
        {
            StreamReader problemFileStream = new StreamReader(problemFilePath);
            // The problem
            instance = VRPModel.LoadFromStream(problemFileStream, problemName,
                Screen.Width - Screen.Width / SCREEN_MARGIN_WIDHT_RATIO,
                Screen.Height - Screen.Height / SCREEN_MARGIN_WIDHT_RATIO
            );
        }
        else if (File.Exists("./Content/VRPLIB.zip"))
        {
            using (ZipArchive zip = ZipFile.Open("./Content/VRPLIB.zip", ZipArchiveMode.Read))
            {
                string fileName = Path.GetFileName(problemFilePath);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.Name == fileName)
                    {
                        StreamReader problemZipStream = new StreamReader( entry.Open() );
                        // The problem
                        instance = VRPModel.LoadFromStream(problemZipStream, problemName,
                            Screen.Width - Screen.Width / SCREEN_MARGIN_WIDHT_RATIO,
                            Screen.Height - Screen.Height / SCREEN_MARGIN_WIDHT_RATIO
                        );
                    }
                }
            }
        }
        
        

        // Determine the max size of a dot
        dotMaxSize = Math.Min(Screen.Width / DOT_MAX_SIZE_RATIO,
                                     Screen.Height / DOT_MAX_SIZE_RATIO);
        for (int i = 1; i < instance.points.Count; i++)
        {
            for (int j = i + 1; j < instance.points.Count; j++)
            {
                double d = Vector.Distance(instance.points[i], instance.points[j]) * DOT_MIN_SIZE_MAX_ALLOWED_OVERLAP;
                if (d > DOT_MIN_SIZE_PX && d < dotMaxSize)
                    dotMaxSize = d;
            }
        }

        for (int i = 0; i < instance.points.Count; i++)
        {
            double hcd = Math.Sqrt(Math.Max(0.5, instance.demands[i] / instance.capacity)) * dotMaxSize + BORDER_WIDTH * 2;
            var hitCircle = new GameObject(hcd, hcd, Shape.Circle);
            hitCircle.IsVisible = false;
            hitCircle.Position = instance.points[i];
            hitCircle.Tag = i;
            Add(hitCircle);

            double d = Math.Sqrt(instance.demands[i] / instance.capacity) * dotMaxSize;
            var customer = new GameObject(d, d, Shape.Circle);
            if (i == 0) customer.Color = DepotColor;
            else customer.Color = NotRoutedColor;
            customer.Tag = "customer";
            customer.Position = instance.points[i];
            Add(customer, -2);

            while (pointToCircleObject.ContainsKey(instance.points[i]))
                instance.points[i] += new Vector(DOT_MIN_SIZE_PX, DOT_MIN_SIZE_PX);
            pointToCircleObject.Add(instance.points[i], customer);

            var border = new GameObject(d + BORDER_WIDTH * 2, d + BORDER_WIDTH*2, Shape.Circle);
            border.Color = BorderColor;
            border.Position = instance.points[i];
            Add(border, -3);
        }
    }


    void ToggleNodeLables()
    {
        if (nodeLabels == null && instance!=null)
        {
            nodeLabels = new List<Label>();
            for (int i = 0; i < instance.points.Count; i++)
            {
                var nodeLabel = new Label(i.ToString());
                nodeLabel.Color = Color.Transparent;
                nodeLabel.Position = instance.points[i];
                Add(nodeLabel, 0);
                nodeLabels.Add(nodeLabel);
            }
        }
        else if (nodeLabels!=null)
        {
            foreach (var nl in nodeLabels)
	        {
                Remove(nl);
	        }
            nodeLabels = null;
        }
    }


    private Label CreateMeterDisplay(Meter meter, Vector position, Image icon)
    {
        Label meterDisplay = new Label(meter);
        meterDisplay.Font = Font.DefaultLarge;
        meterDisplay.Position = position;
        Add(meterDisplay, 3);

        // square icon (height, height) is by intention
        GameObject iconDisplay = new GameObject(meterDisplay.Height, meterDisplay.Height);
        iconDisplay.Image = icon;
        iconDisplay.Position = position - new Vector(meterDisplay.Width / 2 + 10 + iconDisplay.Width / 2, 0);
        Add(iconDisplay, 3);

        meterDisplay.Tag = iconDisplay;

        return meterDisplay;
    }

    #region Solution manipulations
    /// <summary>
    /// Jypeli does not offer a line object and RaySegment does not support line thickness,
    ///  therefore draw the lines/edges with rectangles and some math
    /// </summary>
    /// <param name="edge"></param>
    void RouteEdgeObject(GameObject edge, Vector from, Vector to)
    {
        Vector midPos = from+((to - from) / 2);
        edge.Position = midPos;
        edge.Width = Vector.Distance(from, to);
        double vecToVecRad = 0.0;
        if (to.Y>from.Y)
            vecToVecRad = Math.Acos(Vector.DotProduct((to-from).Normalize(), new Vector(1,0)));
        else
            vecToVecRad = Math.Acos(Vector.DotProduct((to-from).Normalize(), new Vector(-1, 0)));
        edge.Angle = Angle.FromRadians(vecToVecRad);
    }

    void ActivatePoint(int pidx)
    {
        DeactivateOldPoint();

        double d = Math.Sqrt(instance.demands[pidx] / instance.capacity) * dotMaxSize;
        var hotCustomer = new GameObject(d, d, Shape.Circle);
        hotCustomer.Color = Color.Yellow;
        hotCustomer.Position = instance.points[pidx];
        Add(hotCustomer, -1);

        activePointObject = hotCustomer;
        activePointIdx = pidx;

        GameObject newEdge = new GameObject(EDGE_WIDTH, EDGE_WIDTH, Shape.Rectangle);
        newEdge.Color = BorderColor;
        newEdge.Position = hotCustomer.Position;
        Add(newEdge, -3);
        newEdge.Tag = new EdgeEndpoints(pidx, -1);

        dragEdges.Add(newEdge);
    }

    private void DeactivateOldPoint()
    {
        if (activePointObject != null && activePointIdx >= 0)
        {
            Remove(activePointObject);
            activePointObject = null;
            activePointIdx = -1;
        }
    }
    #endregion

    #region Mouse interaction
    GameObject GetEdgeUnderMouse()
    {
        var mp = Mouse.PositionOnWorld;
        var ol = GetObjectsAt(mp, EDGE_HIT_WIDHT);
        var edgeObjects = ol.Where(o => o.Tag is EdgeEndpoints);
        if (edgeObjects.Any())
        {
            return edgeObjects.First();
        }
        else
        {
            return null;
        }
    }

    int GetIndexOfPointUnderMouse()
    {
        var mp = Mouse.PositionOnWorld;
        var ol = GetObjectsAt(mp);
        var hitCircleObjects = ol.Where(o => o.Tag is int);

        double minDist = Double.MaxValue;
        int minIdx = -1;
        foreach (var hio in hitCircleObjects)
        {
            double d = Vector.Distance(mp, hio.Position);
            if (d < minDist)
            {
                minDist = d;
                minIdx = (int)hio.Tag;
            }
        }
        return minIdx;
    }

    void MouseBtnGoesDown()
    {
        int pidx = GetIndexOfPointUnderMouse();
        mouseDownPointIdx = pidx;
        if (pidx >= 0 && pidx != activePointIdx)
        {

            bool passTroughPoint = false;
            if (pidx != VRPModel.DEPOT_IDX && instance.GetNodeNeighbourCount(pidx) >= 2)
            {
                passTroughPoint = true;
            }
            if (passTroughPoint)
            {
                var edge = instance.RemoveExistingEdge(pidx, true);
                if (edge != null) Remove(edge); // remove from game

                RefreshStateAndUpdateDisplays();
                HighlightViolations();
            }
            ActivatePoint(pidx);
        }
        else 
        {
            GameObject edge = GetEdgeUnderMouse();
            if (edge != null) mouseDownEdge = edge;
        }
    }

    void MouseBtnGoesUp()
    {
        int pidx = GetIndexOfPointUnderMouse();
        if (pidx != -1)
        {
            if (pidx == mouseDownPointIdx && pidx != VRPModel.DEPOT_IDX)
            {
                var nbrListClone = new List<int>(instance.GetNodeNeighbours(pidx));
                foreach (int otherEnd in nbrListClone)
                {
                    var removedEdges = instance.RemoveEdges(pidx, otherEnd);
                    foreach (var removedEdge in removedEdges)
                    {
                        Remove(removedEdge); // remove from the game    
                    }
                }
                RefreshStateAndUpdateDisplays();
                HighlightViolations();
            }
        }
        else if (mouseDownEdge!=null)
        {
            if (!dragEdges.Contains(mouseDownEdge) && mouseDownEdge == GetEdgeUnderMouse())
            {
                instance.RemoveEdgeFromSolution(mouseDownEdge);
                Remove(mouseDownEdge); // remove from game
                RefreshStateAndUpdateDisplays();
                HighlightViolations();
            }
        }

        mouseDownEdge = null; 
        mouseDownPointIdx = -1;

        DeactivateOldPoint();

        // Discard all dragged edges
        foreach (var edge in dragEdges)
        {
            Remove(edge);
        }
        dragEdges = new List<GameObject>();
    }
    

    void MouseMoves(AnalogState astate)
    {
        var mp = Mouse.PositionOnWorld;
        foreach (var edge in dragEdges)
        {
            var eps = (EdgeEndpoints)edge.Tag;
            RouteEdgeObject(edge, mp, instance.points[eps.Key]);
        }

        if (activePointIdx >= 0)
        {
            int routeIdx = instance.GetRouteIdxForNode(activePointIdx);
            HighlightOnlyTruck(routeIdx);

            int pidx = GetIndexOfPointUnderMouse();
            List<GameObject> removedEdges = null;
            if (pidx!=-1 && pidx != activePointIdx &&
                instance.GetNodeNeighbourCount(pidx)<2 &&
                instance.ConnectNode(pidx, dragEdges, out removedEdges))
            {
                foreach (var rmEdge in removedEdges)
                {
                    Remove(rmEdge);
                }
                foreach (var edge in dragEdges)
                {
                    var eps = (EdgeEndpoints)edge.Tag;
                    RouteEdgeObject(edge, instance.points[eps.Key], instance.points[eps.Value]);
                }        

                RefreshStateAndUpdateDisplays();
                HighlightViolations();

                // The edges are now considered "used"
                dragEdges = new List<GameObject>();
                ActivatePoint(pidx);
            }    
        }
        else
        {
            var node = GetIndexOfPointUnderMouse();
            if (node != -1)
            {
                bool removed = false;
                List<int> nbrCloneList = new List<int>(instance.GetNodeNeighbours(node));
                foreach (var nbr in nbrCloneList)
                {
                    //var rmEdges = instance.RemoveEdges(node, nbr);
                    //foreach (var rme in rmEdges)
                    //{
                    //    Remove(rme);
                    //    removed = true;
                    //}
                }
                if (removed)
                {
                    RefreshStateAndUpdateDisplays();
                    HighlightViolations();
                }
            }
            
            var edge = GetEdgeUnderMouse();
            int routeIdx = instance.GetRouteIdxForEdge(edge);
            if (routeIdx!=-1)
            {
                HighlightOnlyTruck(routeIdx);
            }
        }
    }

    private void HighlightOnlyTruck(int activeBarIdx)
    {
        if (activeBarIdx >= 0 && activeBarIdx < truckFillBars.Count)
            truckFillBars[activeBarIdx].BorderColor = DepotColor;

        for (int i = 0; i < truckFillBars.Count; i++)
        {
            if (i != activeBarIdx)
                truckFillBars[i].BorderColor = BorderColor;
        }
    }

    #endregion

    #region UI and UX

    void UpdateTruckFillRatios(List<double> fr)
    {
        int tfbIdx = 0;
        foreach (double carriedWt in fr)
        {
            if (tfbIdx>=truckFillBars.Count)
            {
                DoubleMeter truckFillMeter = new DoubleMeter(carriedWt);
                truckFillMeter.MaxValue = instance.capacity;
                ProgressBar truckFillBar = new ProgressBar(
                    Screen.Width - Screen.Width / SCREEN_MARGIN_WIDHT_RATIO, TRUCK_BAR_HEIGHT);
                truckFillBar.Position = new Vector(0, -Screen.Height / 2 + TOP_MARGIN + TRUCK_BAR_HEIGHT * 1.5 * tfbIdx);
                truckFillBar.BindTo(truckFillMeter);
                truckFillBar.BorderColor = BorderColor;
                Add(truckFillBar, 3);
                truckFillBars.Add(truckFillBar);
            }
            else
            {
                (truckFillBars[tfbIdx].Meter as DoubleMeter).Value = carriedWt;
            }

            if (carriedWt > instance.capacity)
            {
                truckFillBars[tfbIdx].BarColor = InfeasibleColor;
            }
            else if (instance.routeValidStates[tfbIdx])
            {
                truckFillBars[tfbIdx].BarColor = CompleteRouteColor;
            }
            else
            {
                truckFillBars[tfbIdx].BarColor = RoutedColor;
            }

            tfbIdx++;
        }
        // Remove unused truck bars (no route)
        while (tfbIdx < truckFillBars.Count)
        {
            Remove(truckFillBars[tfbIdx]);
            truckFillBars.RemoveAt(tfbIdx);
        }
    }

    void UpdateDisplayIconPosition(Label forDisplay)
    {
        GameObject iconDisplay = (GameObject)forDisplay.Tag;
        iconDisplay.Position = forDisplay.Position - new Vector(forDisplay.Width / 2 + 10 + iconDisplay.Width / 2, 0);
    }

    int ShowNewPersonalBest(int new_k, double new_totd)
    {
        string text = "";
        int stars = 0;
        if (new_k <= instance.BKS_k)
        { 
            if (new_totd <= instance.BKS_val*1.05)
            {
                stars = 3;
                text += "Erinomaisesti reititetty.";
                if (new_totd == instance.BKS_val)
                {
                    text += " Itseasiassa löysit optimiratkaisun, eli parhaan mahdollisen reitityksen.";
                }
            }
            else if (new_totd <= instance.BKS_val * 1.20)
            {
                stars = 2;
                text += "Hyvin reititetty. Reittejä voi kuitenkin vielä parantaa. Keksitkö miten?";
            }
            else
            {
                stars = 1;
                text += "Kelpo reititys. Pystytkö muokkaamaan reitit lyhyemmiksi?";
            }
        }
        else if (new_k-1 == instance.BKS_k)
        {
            if (new_totd < instance.BKS_val*1.20)
            {
                stars = 2;
                text += "Hyvin reititetty. Kujetukset voisi vielä silti tehdä pienemmällä määrällä autoja. Keksitkö miten?";
            }
            else 
            {
                stars = 1;
                text += "Kelpo reititys. Pystyt varmasti kuitenkin parantamaan sitä!";
            }
        }
        if (stars > 0)
        {
            Widget levelCompleteWindow = new Widget(Screen.Width/4, Screen.Height/3);
            levelCompleteWindow.Layout = new VerticalLayout();

            var instanceButton = VRPProblemSelector.CreateInstanceButton(Screen.Width / 4, currentInstanceMetadata.ProblemName, stars);
            levelCompleteWindow.Add(instanceButton);
            var infotext = new Label(text);
            infotext.SizeMode = TextSizeMode.Wrapped;
            levelCompleteWindow.Add(infotext);

            Widget continueOrNext = new Widget(new HorizontalLayout());

            var continueBtn = new PushButton("Jatka yrittämistä");
            continueBtn.Clicked += () => Remove(levelCompleteWindow);
            continueOrNext.Add(continueBtn);

            var nextBtn = new PushButton("Seuraava tehtävä");
            nextBtn.Clicked += () => ChooseNextLevel();
            continueOrNext.Add(nextBtn);

            levelCompleteWindow.Add(continueOrNext);

            Add(levelCompleteWindow, -1);
            StarXplosion(1, stars, instanceButton);

            // emulate mb release
            Timer.SingleShot(0.1, ()=>MouseBtnGoesUp());
        }
        return stars;
    }

    private void StarXplosion(int currentStar, int ofStars, GameObject onTopOfTarget)
    {
        if (currentStar>0 && currentStar<=3)
        {
            ExplosionSystem starplosion = new ExplosionSystem(LoadImage("juststar"), 50);
            starplosion.MinVelocity = starplosion.MinVelocity/2;
            starplosion.MaxVelocity = starplosion.MaxVelocity/2;
            Add(starplosion, 3);
            var explosionPosition = new Vector(
                onTopOfTarget.AbsolutePosition.X-onTopOfTarget.Width/2+currentStar*onTopOfTarget.Width/4,
                onTopOfTarget.AbsolutePosition.Y-onTopOfTarget.Height/3);
            onTopOfTarget.Image = LoadImage(String.Format("level_{0}star", currentStar));
            starplosion.AddEffect(explosionPosition, 5+5 * currentStar);

            if (currentStar<ofStars)
            {
                Timer.SingleShot(starplosion.MaxLifetime,
                    ()=>StarXplosion(currentStar+1, ofStars, onTopOfTarget));
            }
        }
    }

    private void RefreshStateAndUpdateDisplays()
    {
        int k;double totd;
        instance.CalculateObjectives(out k, out totd);
        trucksUsed.Value = k; distanceTraveled.Value = totd;
        if (instance.CheckFeasibility())
        {
            ofBKS.Value = (int)((2 * instance.BKS_val - totd) / instance.BKS_val * 100.0);
            ofBKSDisplayNA.IsVisible = false;
            ofBKSDisplay.IsVisible = true;


            if (currentInstanceMetadata.personalBest_k == null ||
                k < currentInstanceMetadata.personalBest_k ||
                (k == currentInstanceMetadata.personalBest_k &&
                totd < currentInstanceMetadata.personalBestSol))
            {
                currentInstanceMetadata.Stars = ShowNewPersonalBest(k, totd);
                currentInstanceMetadata.personalBest_k = k;
                currentInstanceMetadata.personalBestSol = totd;

                if (currentInstanceMetadata.Stars > 0)
                {
                    int ciInx = problemInstanceMetadata.IndexOf(currentInstanceMetadata);
                    if (ciInx + 1 < problemInstanceMetadata.Count)
                        problemInstanceMetadata[ciInx + 1].Locked = false;
                }

                VRPProblemSelector.SavePlayerInstanceDataToFile(problemInstanceMetadata, @"Content\PlayerData.txt");
            }

        }
        else
        { 
            ofBKS.Value = 99;
            ofBKSDisplayNA.IsVisible = true;
            ofBKSDisplay.IsVisible = false;
        }
        UpdateTruckFillRatios(instance.routeFillRatios);
        UpdateDisplayIconPosition(trucksDisplay);
        UpdateDisplayIconPosition(distanceDisplay);
        UpdateDisplayIconPosition(ofBKSDisplay);
    }

    private void HighlightViolations()
    {
        for (int i = 1; i < instance.points.Count; i++)
		{
            int ridx = instance.routeIdxForNodes[i];

            var customer = pointToCircleObject[instance.points[i]];
            if (ridx ==-1)
            {
                customer.Color = NotRoutedColor;
            }
            else if (instance.routeFillRatios[ridx] > instance.capacity)
            {
                customer.Color = InfeasibleColor;
            }
            else
            {
                if (instance.routeValidStates[ridx])
                    customer.Color = CompleteRouteColor;
                else
                    customer.Color = RoutedColor;
            }
		}
    }
    #endregion
}
