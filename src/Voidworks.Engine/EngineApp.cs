using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
using Brutal.OpenGlApi;
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
    private readonly ScenarioOverlay _scenarioOverlay = new();

    private OverlayMode _overlayMode = OverlayMode.Normal;
    private GridCoord? _selectedTile = new GridCoord(3, 3);
    private CrewMember? _selectedCrew;
    private Machine? _selectedMachine;

    private float2 _lastMousePos;
    private bool _isPanning = false;
    private bool _spaceWasPressed = false;
    private bool _leftWasPressed = false;

    public unsafe void Run(SimulationState sim)
    {
        if (!Glfw.Init())
            throw new InvalidOperationException("Failed to initialize GLFW via BRUTAL.");

        Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 4);
        Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 1);
        Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, 0x00032001); // GLFW_OPENGL_CORE_PROFILE

        int initialWidth = 1600;
        int initialHeight = 900;
        if (Glfw.PrimaryMonitor is { } monitor)
        {
            monitor.GetMonitorWorkArea(out int2 workAreaPosition, out int2 workAreaSize);
            if (workAreaSize.X > 0 && workAreaSize.Y > 0)
            {
                initialWidth = Math.Min(initialWidth, Math.Max(960, workAreaSize.X - 80));
                initialHeight = Math.Min(initialHeight, Math.Max(600, workAreaSize.Y - 80));
            }
        }

        var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
        {
            Title = "Voidworks - The Living Hull",
            Size = new int2(initialWidth, initialHeight),
        });

        if (window.IsNull())
        {
            Glfw.Terminate();
            throw new InvalidOperationException(
                "Failed to create GLFW window. Graphical mode requires an OpenGL 4.1 core-profile context.");
        }

        window.MakeContextCurrent();

        if (!Glad.Initialize(ResolveGlFunction))
        {
            Glfw.Terminate();
            throw new InvalidOperationException("Failed to initialize the OpenGL function loader (GLAD).");
        }

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
            if (!WorldInputPolicy.CanControlWorld(io.WantCaptureMouse, false, _scenarioOverlay.BlocksWorldInput))
                return;

            var cur = win.CursorPos;
            var size = win.Size;
            var rawMouse = new float2((float)cur.X, (float)cur.Y);
            var windowSize = new float2(size.X, size.Y);
            var displaySize = io.DisplaySize;
            if (displaySize.X <= 0f || displaySize.Y <= 0f)
                displaySize = windowSize;
            var mouseV = DisplayMetrics.CursorToDisplay(rawMouse, displaySize, windowSize);
            _camera.ZoomAt((float)offset.Y, mouseV, DisplayMetrics.ViewportCenter(displaySize));
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
            bool modalOpen = _scenarioOverlay.BlocksWorldInput;
            bool spaceDown = window.GetKey(GlfwKey.Space) == GlfwKeyAction.Press;
            if (spaceDown && !_spaceWasPressed &&
                WorldInputPolicy.CanControlWorldKeyboard(io.WantCaptureKeyboard, modalOpen))
            {
                sim.IsPaused = !sim.IsPaused;
            }
            _spaceWasPressed = spaceDown;

            // 1. Advance simulation
            sim.Update(deltaTime);

            // 2. Handle input for camera pan and tile/crew/machine selection
            var winSize = window.Size;
            var windowSizeF = new float2(winSize.X, winSize.Y);
            var displaySize = io.DisplaySize;
            if (displaySize.X <= 0f || displaySize.Y <= 0f)
                displaySize = windowSizeF;

            var cursor = window.CursorPos;
            var mousePos = DisplayMetrics.CursorToDisplay(
                new float2((float)cursor.X, (float)cursor.Y),
                displaySize,
                windowSizeF);
            var viewportCenter = DisplayMetrics.ViewportCenter(displaySize);

            // Middle mouse panning. Capture is sampled before the UI frame, so a drag
            // that starts over a panel must not move the world underneath it.
            bool middleDown = window.GetMouseButton(GlfwMouseButton.Middle) == GlfwButtonAction.Press;
            if (middleDown &&
                WorldInputPolicy.CanStartPan(io.WantCaptureMouse, modalOpen, _isPanning))
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
            bool canControlWorld = WorldInputPolicy.CanControlWorld(
                io.WantCaptureMouse, false, modalOpen);
            if (canControlWorld)
            {
                hoveredCoord = _camera.ScreenToTile(mousePos, viewportCenter);

                bool leftDown = window.GetMouseButton(GlfwMouseButton.Left) == GlfwButtonAction.Press;
                if (leftDown && !_leftWasPressed &&
                    hoveredCoord.HasValue && sim.Grid.IsInBounds(hoveredCoord.Value))
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
                _leftWasPressed = leftDown;
            }
            else
            {
                _leftWasPressed = window.GetMouseButton(GlfwMouseButton.Left) == GlfwButtonAction.Press;
            }

            // 3. ImGui Frame
            glfwBackend.NewFrame();
            glBackend.NewFrame();
            ImGui.NewFrame();

            // Render 2D Viewport into ImGui background
            var drawList = ImGui.GetBackgroundDrawList();
            drawList.AddRectFilled(float2.Zero, displaySize, _renderer.ColorSpace);

            _renderer.Render(
                drawList,
                sim,
                _camera,
                viewportCenter,
                hoveredCoord,
                _selectedTile,
                _overlayMode);

            // Render interactive UI panels
            _ui.HideForScenarioOverlay = _scenarioOverlay.BlocksWorldInput;
            _ui.Render(sim, ref _overlayMode, ref _selectedTile, ref _selectedCrew, ref _selectedMachine);
            _scenarioOverlay.Render(sim);
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
            UiText.ResetFrameArena();
        }

        glBackend.Dispose();
        glfwBackend.Dispose();
        ImGui.DestroyContext();
        window.Dispose();
        Glfw.Terminate();
    }

    private static unsafe IntPtr ResolveGlFunction(byte* name)
        => Glfw.GetProcAddress(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(name));
}
