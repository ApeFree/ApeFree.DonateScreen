using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenCapture.NET;
using System;
using System.Linq;
using System.Reflection;

namespace ApeFree.DonateScreen.Tests
{
    [TestClass]
    public class ScreenCaptureSmokeTest
    {
        [TestMethod]
        public void TestScreenCaptureServiceInitialization()
        {
            var screenCaptureService = new DX11ScreenCaptureService();
            var graphicsCards = screenCaptureService.GetGraphicsCards().ToList();
            Assert.IsNotNull(graphicsCards);
            Console.WriteLine($"Found {graphicsCards.Count} graphics cards.");

            if (graphicsCards.Count > 0)
            {
                var card = graphicsCards[0];
                var props = card.GetType().GetProperties();
                foreach (var p in props)
                {
                    Console.WriteLine($"Property: {p.Name} ({p.PropertyType})");
                }
                var methods = card.GetType().GetMethods();
                foreach (var m in methods)
                {
                    Console.WriteLine($"Method: {m.Name}");
                }
            }
        }
    }
}
