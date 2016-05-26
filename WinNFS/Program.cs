using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinNFS
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Terdos.WinNFS.NFSProxy proxy = new Terdos.WinNFS.NFSProxy(new System.Net.IPAddress(new byte[] { 192, 168, 0, 110 }));
                foreach (String device in proxy.GetExportedDevices())
                {
                    Console.WriteLine(device);
                }
                proxy.Mount("/home", "n:\\");//, DokanNet.DokanOptions.DebugMode, 5);

                Console.WriteLine("Success");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
