using System;
using System.Reflection;
using XNA_GDM = Microsoft.Xna.Framework.GraphicsDeviceManager;
using XNA_Window = Microsoft.Xna.Framework.GameWindow;
static class App
{
    static XNA_GDM UglyHackToGetGraphicsDeviceManagerWithReflection()
    {
        BindingFlags flgs = BindingFlags.NonPublic | BindingFlags.FlattenHierarchy |
            BindingFlags.DeclaredOnly | BindingFlags.Static;
        FieldInfo[] _fields = typeof(Jypeli.Game).GetFields(flgs);
        Console.WriteLine("{0} fields:", _fields.Length);
        foreach (FieldInfo fi in _fields)
        {
            Console.WriteLine(fi.Name);
            if (fi.FieldType == typeof(XNA_GDM))
            {

                return (XNA_GDM)(fi.GetValue(null));
            }
        }
        return null;
    }

    static XNA_Window UglyHackToGetXNAGAmeWindowWithReflection(Jypeli.JypeliWindow win)
    {
        // Get value of an instance from the base class type (confused yet?)
        BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo[] _fields = typeof(Jypeli.JypeliWindow).GetFields(bindFlags);
        Console.WriteLine("{0} fields:", _fields.Length);
        foreach (FieldInfo fi in _fields)
        {
            Console.WriteLine(fi.Name);
            if (fi.FieldType == typeof(XNA_Window))
            {

                return (XNA_Window)(fi.GetValue(win));
            }
        }
        return null;
    }

    static Jypeli.JypeliWindow UglyHackToInstantiateJypeliWindow(XNA_Window gwin, XNA_GDM gdm)
    {
        ConstructorInfo jpwCtr = typeof(Jypeli.JypeliWindow).GetConstructor(
                      BindingFlags.NonPublic | BindingFlags.Instance,
                      null, new Type[] { typeof(XNA_Window), typeof(XNA_GDM) }, null);

        Jypeli.JypeliWindow jpw = (Jypeli.JypeliWindow)(jpwCtr.Invoke(new object[] { gwin, gdm }));
        return jpw;
    }

    static void UglyHackToSwitchJypeliWindowWithReflection(Jypeli.JypeliWindow aawin)
    {
        BindingFlags flgs = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy |
            BindingFlags.DeclaredOnly | BindingFlags.Static;
        FieldInfo[] _fields = typeof(Jypeli.Game).GetFields(flgs);
        Console.WriteLine("{0} fields:", _fields.Length);
        foreach (FieldInfo fi in _fields)
        {
            Console.WriteLine(fi.Name);
            if (fi.FieldType == typeof(Jypeli.JypeliWindow))
            {
                fi.SetValue(null, aawin);
                break;
                //property.GetSetMethod(true).Invoke(game, new object[] { antialiasedWindow });
            }
        }
    }

    private static void TryToSwitchAntialiasingOn()
    {
        // The GDM creates the graphicsdevices for windows to draw. Gain access to it.
        XNA_GDM gdm = UglyHackToGetGraphicsDeviceManagerWithReflection();
        gdm.PreferMultiSampling = true;
        gdm.ApplyChanges();

        // Ok, now lets switch the Jypeli.Game.Window before anyone noitices!
        XNA_Window gwin = UglyHackToGetXNAGAmeWindowWithReflection(VRPGame.Window);

        Jypeli.JypeliWindow antialiasedWindow = UglyHackToInstantiateJypeliWindow(gwin, gdm);

        UglyHackToSwitchJypeliWindowWithReflection(antialiasedWindow);

        // TODO: If everything else works do this e.g. with more reflection
        //  http://stackoverflow.com/questions/660480/determine-list-of-event-handlers-bound-to-event
        //Window.Resizing += new JypeliWindow.ResizeEvent( WindowResized );
        //Window.Resized += new JypeliWindow.ResizeEvent( WindowResized );
            
    }

#if WINDOWS || XBOX
    static void Main(string[] args)
    {
        using (VRPGame game = new VRPGame())
        {
            //TryToSwitchAntialiasingOn();
#if !DEBUG
            //game.IsFullScreen = true;
#endif
            game.Run();
        }
    }
#endif
}
