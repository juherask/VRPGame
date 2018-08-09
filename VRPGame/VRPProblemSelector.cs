using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jypeli;
using Jypeli.Widgets;
using System.Globalization;
using System.IO;

class VRPProblemSelector
{
    static Image levelLockedImage = Game.LoadImage("level_locked");
    static Image levelBtn0Image = Game.LoadImage("level_nostar");
    static Image levelBtn1Image = Game.LoadImage("level_1star");
    static Image levelBtn2Image = Game.LoadImage("level_2star");
    static Image levelBtn3Image = Game.LoadImage("level_3star");

    public class ProblemInstance
    {
        // load from datafile
        public string ProblemSet;
        public string ProblemName;
        public string ProblemFile;
        public string SolutionFile;

        public double Difficulty;
        public double Size;
        public double Tightness;
        public double BKS;
        public bool BKSisOptimal;
        public double CpuSolveTime;

        // Load from another file
        public bool Locked;
        public int Stars;
        public double? personalBestSol;
        public double? personalBest_k;
    }

    static int MAX_ROWS = 4;
    static int SPACING_BETWEEN_BTNS = 15;
    static string CVRBLIBFOLDER = @".\Content\VRPLIB";
    static double MAX_LOG_T_VAL = 6.0; // TODO: find a "logarithmlike" func with limit, sigmoind? http://math.stackexchange.com/questions/925979/how-can-i-construct-a-specific-sigmoid-function
    VRPGame game;

    public static void SavePlayerInstanceDataToFile(List<ProblemInstance> data, string dataFilePath)
    {
        using (StreamWriter file =
            new StreamWriter(dataFilePath))
        {
            file.WriteLine("ProblemName;Locked;Stars;personalBestSol;personalBest_k");
            foreach (var pi in data)
            {
                // If the line doesn't contain the word 'Second', write the line to the file.
                if (!pi.Locked)
                {
                    var line = String.Format(CultureInfo.InvariantCulture, 
                        "{0};{1};{2};{3};{4}",
                        pi.ProblemName, pi.Locked, pi.Stars, pi.personalBestSol, pi.personalBest_k);
                    file.WriteLine(line);
                }
            }
        }
    }

    public static void LoadPlayerInstanceDataFromFile(string dataFilePath, List<ProblemInstance> toCompleteData)
    {
        // The file might be missing the first time.
        if (!File.Exists(dataFilePath)) return;

        // Read the file, line by line, ignoring the header line
        bool firstLine = true;
        string line;
        StreamReader file = new StreamReader(dataFilePath);
        while ((line = file.ReadLine()) != null)
        {
            if (firstLine)
            {
                // Header
                firstLine = false;
                continue;
            }

            // eg. gr-n17-k3;False;1;3475;4;
            var parts = line.Trim().Split(';');
            string problemName = parts[0];
            var problemInstance = (toCompleteData.Where(pi=>pi.ProblemName==problemName)).First();

            problemInstance.Locked = bool.Parse( parts[1] );
            problemInstance.Stars = Int32.Parse(parts[2]);
            if (3<parts.Count() && parts[3].Trim() != "")
            {
                problemInstance.personalBestSol = Double.Parse(parts[3], CultureInfo.InvariantCulture);
            }
            if (4 < parts.Count() && parts[4].Trim() != "")
            {
                problemInstance.personalBest_k = Int32.Parse(parts[4]);
            }
        }
    }

    public static List<ProblemInstance> LoadInstanceDataFromFile(string dataFilePath)
    {
        
        List<ProblemInstance> instances = new List<ProblemInstance>();
        List<string> problemSets = new List<string>();

        bool firstLine = true;
        string line;
        StreamReader file = new StreamReader(dataFilePath);
        while ((line = file.ReadLine()) != null)
        {
            if (firstLine)
            {
                // Header
                firstLine = false;
                continue;
            }

            //ProblemSet;ProblemName;ProblemFile;SolutionFile;Type;Size;Tightness;Optimal;BKS;CpuSolveTime
            //A;A-n32-k5;A-n32-k5.vrp;A-n32-k5.opt;U;32;0.82;784;784;5.33
            //0 1        2            3            4 5  6    7   8   9    
            var parts = line.Trim().Split(';');
            string problemfolder = CVRBLIBFOLDER + '\\' + parts[0];
            double typeMultiplier =  parts[4]=="U" ? 
                1.0 : // uniform = hard
                ((parts[4]=="C") ? 0.5 : 0.75);  // clustered = easy, other other.
            double optval = Double.Parse(parts[7], CultureInfo.InvariantCulture);
            var readProblem = new ProblemInstance() {
                ProblemSet = parts[0],
                ProblemName = parts[1],
                ProblemFile = problemfolder + '\\' + parts[2],
                SolutionFile = problemfolder + '\\' + parts[3],

                Size = Int32.Parse(parts[5]),
                BKS = Double.Parse(parts[8], CultureInfo.InvariantCulture),
                Tightness = Double.Parse(parts[6], CultureInfo.InvariantCulture),
                CpuSolveTime = Double.Parse(parts[9], CultureInfo.InvariantCulture),

                // TODO: load these from another file
                Locked = true,
                Stars = 0,
                personalBestSol = null,
                personalBest_k = null,
            };

            if(!problemSets.Contains(readProblem.ProblemSet))
            {
                readProblem.Locked = false;
                problemSets.Add(readProblem.ProblemSet);
            }
            

            readProblem.Difficulty = 
                0.33*readProblem.Size/200 +
                0.33*Math.Sqrt(readProblem.Tightness) * typeMultiplier +
                0.33*Math.Log10(readProblem.CpuSolveTime) / MAX_LOG_T_VAL;

            readProblem.BKSisOptimal = readProblem.BKS==optval;
            instances.Add( readProblem);

        }
        return instances;
    }
    public VRPProblemSelector(VRPGame game)
    {
        this.game = game;
    }

    public delegate void ProblemSelectedHandler(ProblemInstance selected);
    public delegate void SetSelectedHandler(string selected);

    public void ViewProblemSelect(List<ProblemInstance> instances, ProblemSelectedHandler handler, int perRow = 6)
    {
        var selh = Game.Screen.Height / VRPGame.SCREEN_MARGIN_WIDHT_RATIO;

        // int perRow = Math.Max(1,instances.Count/MAX_ROWS);

        double btnSize = (Game.Screen.Width - SPACING_BETWEEN_BTNS * (perRow + 3)) / perRow;

        var vl = new VerticalLayout();
        vl.Spacing = SPACING_BETWEEN_BTNS;
        Widget vgrid = new Widget(vl);
        for (int i = 0; i < MAX_ROWS && i*perRow<instances.Count; i++)
		{
            var hl = new HorizontalLayout();
            hl.Spacing = SPACING_BETWEEN_BTNS;
            Widget hgrid = new Widget(hl);

            for (int j = i * perRow; j < (i + 1) * perRow && j < instances.Count; j++)
            {
                var probI = instances[j];
                PushButton selectThisProblemBtn = VRPProblemSelector.CreateInstanceButton(btnSize, probI.ProblemName, probI.Stars, probI.Locked);

                if (!probI.Locked)
                {
                    selectThisProblemBtn.Clicked += () => handler(probI);
                    selectThisProblemBtn.Clicked += () => game.Remove(vgrid);
                }

                hgrid.Add(selectThisProblemBtn);
            }
			vgrid.Add(hgrid);
		}
        game.Add(vgrid, -1);
    }

    public static PushButton CreateInstanceButton(double btnSize, string name, int stars, bool locked=false)
    {
        PushButton problemBtn = new PushButton(btnSize, btnSize, name);
        //selectThisProblemBtn.SizeMode = TextSizeMode.None;

        if (locked)
        {
            problemBtn.Image = levelLockedImage;
        }
        else
        { 
            switch (stars)
            {
                case 0:
                    problemBtn.Image = levelBtn0Image;
                    break;
                case 1:
                    problemBtn.Image = levelBtn1Image;
                    break;
                case 2:
                    problemBtn.Image = levelBtn2Image;
                    break;
                case 3:
                    problemBtn.Image = levelBtn3Image;
                    break;
                default:
                    break;
            }
        }
        return problemBtn;
    }


    public void ViewSetSelect(List<ProblemInstance> instances, SetSelectedHandler handler, int perRow = 4)
    {
        var selh = Game.Screen.Height / VRPGame.SCREEN_MARGIN_WIDHT_RATIO;

        // int perRow = Math.Max(1,instances.Count/MAX_ROWS);
        List<string> sets = instances.Select(pi => pi.ProblemSet).Distinct().ToList();

        double btnSize = (Game.Screen.Width - SPACING_BETWEEN_BTNS * (perRow + 3)) / perRow;

        var vl = new VerticalLayout();
        vl.Spacing = SPACING_BETWEEN_BTNS;
        Widget vgrid = new Widget(vl);
        for (int i = 0; i < MAX_ROWS && i * perRow < sets.Count; i++)
        {
            var hl = new HorizontalLayout();
            hl.Spacing = SPACING_BETWEEN_BTNS;
            Widget hgrid = new Widget(hl);

            for (int j = i * perRow; j < (i + 1) * perRow && j < sets.Count; j++)
            {
                string setName = sets[j];
                PushButton selectThisSetBtn = new PushButton(btnSize, btnSize, setName);
                selectThisSetBtn.Clicked += () => handler(setName);
                selectThisSetBtn.Clicked += () => game.Remove(vgrid);
                hgrid.Add(selectThisSetBtn);

            }
            vgrid.Add(hgrid);
        }
        game.Add(vgrid);
    }
}
