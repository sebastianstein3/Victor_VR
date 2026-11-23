using CardboardXR.Demos;
using CardboardXR.Demos.Workspace;
using UnityEngine;

namespace ProjectBootstrap
{
    // Spatial Workspace: floating virtual screens and an interactive desk keyboard.
    class WorkspaceSetup : DemoSetup
    {
        public override string Name => "Workspace";
        public override int Order => 15;

        public override DemoCatalog.Entry Entry => new()
        {
            Id = "workspace",
            Title = "Spatial Workspace",
            Description = "Wirtualne ekrany w przestrzeni i wirtualna klawiatura rzutowana na blat biurka. Pisz stukając palcem w stół i rozmieszczaj monitory w pokoju.",
            Tagline = "Wirtualne monitory i klawiatura na Twoim biurku",
            Icon = "touch_app",
            Tint = new Color(0.18f, 0.65f, 0.92f),
            Requirements = new DemoRequirements
            {
                SixDof = Need.On,
                Passthrough = Need.On,
                Hands = Need.On,
                // Nothing reads the scanned surfaces yet (the keyboard floats); scanning would only cost
                // CPU and pause hand tracking when it starts. Turn on with "keyboard on the real desk".
                ScanSurfaces = false
            },
        };

        public override void BuildScene(string folder)
        {
            new GameObject("Workspace Demo").AddComponent<WorkspaceDemo>();
        }
    }
}
