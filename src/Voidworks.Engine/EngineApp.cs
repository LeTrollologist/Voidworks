using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
using Voidworks.Sim;
using Voidworks.Sim.Crew;
using Voidworks.Sim.Machinery;
using Voidworks.Sim.Persistence;
using Voidworks.Sim.Ship;

namespace Voidworks.Engine;

public sealed class EngineApp
{
    private readonly Camera2D _camera = new();
    private readonly ViewportRenderer _renderer = new();
    private readonly InspectionPanel _ui = new();

    private OverlayMode _overlayMode = OverlayMode.Normal;
    private GridCoord? _selectedTile = new GridCoord(3, 3);
    private CrewMember? _selectedCrew;
    private Machine? _selectedMachine;

    private float2 _lastMousePos;
    private bool _isPanning = false;
    private bool _spaceWasPressed = false;

    public void Run(SimulationState sim)
    {
        if (!Glfw.Init())
            throw new InvalidOperationException("Failed to initialize GLFW via BRUTAL.");

        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 3);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 3);

        int initialWidth = 1600;
        int initialHeight = 900;

        var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
        {
            Title = "Voidworks - The Living Hull",
            Size = new int2(initialWidth, initialHeight),
        });

        if (window.IsNull())
        {
            Glfw.Terminate();
            throw new InvalidOperationException("Failed to create GLFW window.");
        }

        window.MakeContextCurrent();

        // Center camera on ship
        _camera.WorldCenter = new float2(sim.Grid.Width * 0.5f, sim.Grid.Height * 0.5f);
        _camera.Zoom = 52f;

        // Select initial crew if present
        if (sim.Crew.Count > 0)
        {
            _selectedCrew = sim.Crew[0].Crew;
        }

        // Setup ImGui
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        var glfwBackend = new GlfwBackend(window, GlfwBackend.ClientApi.OpenGL);
        var glBackend = new OpenGlBackend();

        // Setup mouse wheel scroll event for camera zoom
        window.OnScroll += (win, offset) =>
        {
            if (!io.WantCaptureMouse)
            {
                var cur = win.CursorPos;
                var mouseV = new float2((float)cur.X, (float)cur.Y);
                var size = win.Size;
                var center = new float2(size.X * 0.5f, size.Y * 0.5f);
                _camera.ZoomAt((float)offset.Y, mouseV, center);
            }
        };

        var stopwatch = Stopwatch.StartNew();
        double lastTime = stopwatch.Elapsed.TotalSeconds;

        while (!window.ShouldClose)
        {
            Glfw.PollEvents();

            double currentTime = stopwatch.Elapsed.TotalSeconds;
            double deltaTime = currentTime - lastTime;
            lastTime = currentTime;

            // Spacebar pause toggle
            bool spaceDown = window.GetKey(GlfwKey.Space) == GlfwKeyAction.Press;
            if (spaceDown && !_spaceWasPressed && !io.WantCaptureKeyboard)
            {
                sim.IsPaused = !sim.IsPaused;
            }
            _spaceWasPressed = spaceDown;

            // 1. Advance simulation
            sim.Update(deltaTime);

            // 2. Handle input for camera pan and tile/crew/machine selection
            var winSize = window.Size;
            var viewportSize = new float2(winSize.X, winSize.Y);
            var viewportCenter = viewportSize * 0.5f;

            var cursor = window.CursorPos;
            var mousePos = new float2((float)cursor.X, (float)cursor.Y);

            // Middle mouse panning
            bool middleDown = window.GetMouseButton(GlfwMouseButton.Middle) == GlfwButtonAction.Press;
            if (middleDown)
            {
                if (_isPanning)
                {
                    var delta = mousePos - _lastMousePos;
                    _camera.Pan(delta);
                }
                _isPanning = true;
            }
            else
            {
                _isPanning = false;
            }
            _lastMousePos = mousePos;

            // Hover and selection
            GridCoord? hoveredCoord = null;
            if (!io.WantCaptureMouse)
            {
                hoveredCoord = _camera.ScreenToTile(mousePos, viewportCenter);

                if (window.GetMouseButton(GlfwMouseButton.Left) == GlfwButtonAction.Press)
                {
                    if (hoveredCoord.HasValue && sim.Grid.IsInBounds(hoveredCoord.Value))
                    {
                        _selectedTile = hoveredCoord.Value;

                        // Check if a crew member is on this tile
                        var crewOnTile = sim.Crew.FirstOrDefault(c => c.Crew.Position == hoveredCoord.Value);
                        if (crewOnTile != null)
                        {
                            _selectedCrew = crewOnTile.Crew;
                        }

                        // Check if a machine is on this tile
                        var machOnTile = sim.Machines.FirstOrDefault(m => m.Position == hoveredCoord.Value);
                        if (machOnTile != null)
                        {
                            _selectedMachine = machOnTile;
                        }
                    }
                }
            }

            // 3. ImGui Frame
            glfwBackend.NewFrame();
            glBackend.NewFrame();
            ImGui.NewFrame();

            // Render 2D Viewport into ImGui background
            var drawList = ImGui.GetBackgroundDrawList();
            drawList.AddRectFilled(float2.Zero, viewportSize, _renderer.ColorSpace);

            _renderer.Render(
                drawList,
                sim,
                _camera,
                viewportCenter,
                hoveredCoord,
                _selectedTile,
                _overlayMode);

            // Render interactive UI panels
            _ui.Render(sim, ref _overlayMode, ref _selectedTile, ref _selectedCrew, ref _selectedMachine);
            if (_ui.LoadRequestedPath is { } loadPath)
            {
                try
                {
                    sim = SimulationSerializer.LoadFromFile(loadPath);
                    _selectedCrew = sim.Crew.FirstOrDefault()?.Crew;
                    _selectedMachine = null;
                    _selectedTile = _selectedCrew?.Position;
                    _ui.SetPersistenceStatus("Ship loaded.");
                }
                catch (Exception ex)
                {
                    _ui.SetPersistenceStatus($"Load failed: {ex.Message}");
                }
                finally
                {
                    _ui.ClearLoadRequest();
                }
            }

            // 4. Render and Swap
            ImGui.Render();
            glBackend.RenderDrawData(ImGui.GetDrawData());
            window.SwapBuffers();
        }

        glBackend.Dispose();
        glfwBackend.Dispose();
        ImGui.DestroyContext();
        window.Dispose();
        Glfw.Terminate();
    }
}
