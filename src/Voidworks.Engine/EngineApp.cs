using System;
using System.Collections.Generic;
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
using Voidworks.Sim.Commands;
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
    private bool _rightWasPressed = false;
    private bool _escapeWasPressed = false;
    private bool _bWasPressed = false;
    private bool _zWasPressed = false;
    private bool _oWasPressed = false;
    private bool _fitCameraRequested = true;
    private GridCoord? _lastPaintTile;
    private bool _lastPaintRemove;
    private GridCoord? _debugHoverTile;
    private bool _debugTerminalLogged;
    private static readonly bool UiDebug =
        Environment.GetEnvironmentVariable("VOIDWORKS_UI_DEBUG") == "1";
    private readonly Stack<UtilityUndo> _placementUndo = new();

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
            Title = $"Voidworks {BuildInfo.Version} - The Living Hull",
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
            LayoutRect worldViewport = WorkstationLayout.WorldViewport(displaySize);
            if (_fitCameraRequested)
            {
                _camera.Fit(
                    float2.Zero,
                    new float2(sim.Grid.Width, sim.Grid.Height),
                    new float2(
                        worldViewport.X + worldViewport.Width * 0.5f,
                        worldViewport.Y + worldViewport.Height * 0.5f),
                    worldViewport.Size);
                _fitCameraRequested = false;
            }
            var mouseV = DisplayMetrics.CursorToDisplay(rawMouse, displaySize, windowSize);
            _camera.ZoomAt(
                (float)offset.Y,
                mouseV,
                new float2(
                    worldViewport.X + worldViewport.Width * 0.5f,
                    worldViewport.Y + worldViewport.Height * 0.5f));
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

            bool bDown = window.GetKey(GlfwKey.B) == GlfwKeyAction.Press;
            if (bDown && !_bWasPressed && !modalOpen && !io.WantCaptureKeyboard)
                _ui.ToggleBuildPalette();
            _bWasPressed = bDown;

            bool ctrlDown = window.GetKey(GlfwKey.LeftControl) == GlfwKeyAction.Press ||
                            window.GetKey(GlfwKey.RightControl) == GlfwKeyAction.Press;
            bool zDown = window.GetKey(GlfwKey.Z) == GlfwKeyAction.Press;
            if (ctrlDown && zDown && !_zWasPressed && !modalOpen)
                UndoLastUtility(sim);
            _zWasPressed = zDown;

            bool oDown = window.GetKey(GlfwKey.O) == GlfwKeyAction.Press;
            if (oDown && !_oWasPressed && !modalOpen && !io.WantCaptureKeyboard &&
                _selectedMachine != null)
            {
                var result = sim.ExecuteCommand(
                    new SetMachineOnlineCommand(_selectedMachine.Id, !_selectedMachine.IsOnline));
                _ui.SetCommandStatus(result.Message);
                if (UiDebug)
                    Console.WriteLine($"[UI] Machine toggle: {result.Message}");
            }
            _oWasPressed = oDown;

            if (!modalOpen && !io.WantCaptureKeyboard)
            {
                PlacementState hotkeyPlacement = _ui.Placement;
                if (window.GetKey(GlfwKey.Number1) == GlfwKeyAction.Press)
                    hotkeyPlacement.SelectUtility(PlacementTool.PowerCable);
                else if (window.GetKey(GlfwKey.Number2) == GlfwKeyAction.Press)
                    hotkeyPlacement.SelectUtility(PlacementTool.CoolantPipe);
                else if (window.GetKey(GlfwKey.Number3) == GlfwKeyAction.Press)
                    hotkeyPlacement.SelectUtility(PlacementTool.PropellantPipe);
                else if (window.GetKey(GlfwKey.Number4) == GlfwKeyAction.Press)
                    hotkeyPlacement.SelectMachine(MachineType.Battery);
                else if (window.GetKey(GlfwKey.Number5) == GlfwKeyAction.Press)
                    hotkeyPlacement.SelectMachine(MachineType.OxygenGenerator);
            }

            // 1. Advance simulation
            sim.Update(deltaTime);
            if (UiDebug && sim.ActiveScenario is { } debugScenario &&
                (debugScenario.IsCompleted || debugScenario.IsFailed) && !_debugTerminalLogged)
            {
                _debugTerminalLogged = true;
                Console.WriteLine(debugScenario.IsCompleted
                    ? $"[UI] MISSION COMPLETE at {debugScenario.ElapsedSeconds:F1}s"
                    : $"[UI] MISSION FAILED at {debugScenario.ElapsedSeconds:F1}s: {debugScenario.FailureReason}");
            }

            // 2. Handle input for camera pan and tile/crew/machine selection
            var winSize = window.Size;
            var windowSizeF = new float2(winSize.X, winSize.Y);
            var displaySize = io.DisplaySize;
            if (displaySize.X <= 0f || displaySize.Y <= 0f)
                displaySize = windowSizeF;

            LayoutRect worldViewport = WorkstationLayout.WorldViewport(displaySize);
            if (_fitCameraRequested)
            {
                _camera.Fit(
                    float2.Zero,
                    new float2(sim.Grid.Width, sim.Grid.Height),
                    new float2(
                        worldViewport.X + worldViewport.Width * 0.5f,
                        worldViewport.Y + worldViewport.Height * 0.5f),
                    worldViewport.Size);
                _fitCameraRequested = false;
            }

            var cursor = window.CursorPos;
            var mousePos = DisplayMetrics.CursorToDisplay(
                new float2((float)cursor.X, (float)cursor.Y),
                displaySize,
                windowSizeF);
            var viewportCenter = new float2(
                worldViewport.X + worldViewport.Width * 0.5f,
                worldViewport.Y + worldViewport.Height * 0.5f);

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

            // Hover, placement, and selection
            GridCoord? hoveredCoord = null;
            PlacementPreview? placementPreview = null;
            bool canControlWorld = WorldInputPolicy.CanControlWorld(
                io.WantCaptureMouse, false, modalOpen);
            PlacementState placement = _ui.Placement;
            bool leftDown = window.GetMouseButton(GlfwMouseButton.Left) == GlfwButtonAction.Press;
            bool rightDown = window.GetMouseButton(GlfwMouseButton.Right) == GlfwButtonAction.Press;
            bool leftClicked = leftDown && !_leftWasPressed;
            bool rightClicked = rightDown && !_rightWasPressed;
            bool escapeDown = window.GetKey(GlfwKey.Escape) == GlfwKeyAction.Press;

            if (!modalOpen && escapeDown && !_escapeWasPressed && placement.IsActive)
                placement.Cancel();
            _escapeWasPressed = escapeDown;

            if (canControlWorld)
            {
                hoveredCoord = _camera.ScreenToTile(mousePos, viewportCenter);
                if (UiDebug && hoveredCoord != _debugHoverTile)
                {
                    _debugHoverTile = hoveredCoord;
                    Console.WriteLine($"[UI] Hover {hoveredCoord}");
                }

                if (placement.IsActive && hoveredCoord.HasValue)
                {
                    GridCoord target = hoveredCoord.Value;
                    bool valid = PlacementRules.CanApply(sim, placement, target);
                    bool removing = rightDown && !placement.IsMachine;
                    placementPreview = new PlacementPreview(
                        target,
                        GetPlacementColor(placement, valid, removing),
                        placement.Label);
                }
                else if (!placement.IsActive && leftClicked &&
                         hoveredCoord.HasValue && sim.Grid.IsInBounds(hoveredCoord.Value))
                {
                    _selectedTile = hoveredCoord.Value;

                    var crewOnTile = sim.Crew.FirstOrDefault(c => c.Crew.Position == hoveredCoord.Value);
                    _selectedCrew = crewOnTile?.Crew;

                    var machOnTile = sim.Machines.FirstOrDefault(m => m.Position == hoveredCoord.Value);
                    _selectedMachine = machOnTile;
                    if (UiDebug)
                        Console.WriteLine(
                            $"[UI] Selected {hoveredCoord.Value}; machine={machOnTile?.Name ?? "none"}; crew={crewOnTile?.Crew.Name ?? "none"}");
                }
            }
            else
            {
                _lastPaintTile = null;
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
                _overlayMode,
                placementPreview);

            // Render interactive UI panels
            _ui.HideForScenarioOverlay = _scenarioOverlay.BlocksWorldInput;
            _ui.Render(sim, ref _overlayMode, ref _selectedTile, ref _selectedCrew, ref _selectedMachine, hoveredCoord);
            if (_ui.FitViewRequested)
                _fitCameraRequested = true;
            if (_ui.UndoRequested)
                UndoLastUtility(sim);
            _scenarioOverlay.Render(sim);

            // Placement is applied after the UI frame so a click on the palette can
            // never leak through to the world. Hover state is now current, not last-frame.
            bool pointerOverUi = ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow) ||
                                 _scenarioOverlay.BlocksWorldInput;
            if (placement.IsActive && !pointerOverUi && !modalOpen && hoveredCoord.HasValue)
            {
                GridCoord target = hoveredCoord.Value;
                if (placement.IsMachine)
                {
                    if (rightClicked)
                        placement.Cancel();
                    else if (leftClicked)
                        PlaceMachine(sim, placement, target);
                }
                else if (leftDown || rightDown)
                {
                    bool removing = rightDown;
                    if (_lastPaintTile != target || _lastPaintRemove != removing)
                    {
                        PlaceUtility(sim, placement, target, removing);
                        _lastPaintTile = target;
                        _lastPaintRemove = removing;
                    }
                }
                else
                {
                    _lastPaintTile = null;
                }
            }
            else if (leftDown || rightDown)
            {
                _lastPaintTile = null;
            }

            _leftWasPressed = leftDown;
            _rightWasPressed = rightDown;

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
                    // A crashed save can leave a complete temporary file behind. Try
                    // to promote it once before reporting the original failure.
                    string? recovered = SaveRecovery.TryRecoverInterruptedSave(loadPath, out string recoveryMessage);
                    if (recovered != null)
                    {
                        try
                        {
                            sim = SimulationSerializer.LoadFromFile(recovered);
                            _selectedCrew = sim.Crew.FirstOrDefault()?.Crew;
                            _selectedMachine = null;
                            _selectedTile = _selectedCrew?.Position;
                            _ui.SetPersistenceStatus($"Recovered interrupted save. {recoveryMessage}");
                        }
                        catch (Exception recoveryFailure)
                        {
                            _ui.SetPersistenceStatus(
                                $"Load failed ({loadPath}): {ex.Message} Recovery failed: {recoveryFailure.Message}");
                        }
                    }
                    else
                    {
                        _ui.SetPersistenceStatus($"Load failed ({loadPath}): {ex.Message} {recoveryMessage}");
                    }
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

    private void PlaceUtility(
        SimulationState sim,
        PlacementState placement,
        GridCoord target,
        bool remove)
    {
        if (!PlacementRules.CanApply(sim, placement, target))
        {
            _ui.SetCommandStatus($"Cannot place {placement.Label} at {target}.");
            return;
        }

        UtilityKind kind = PlacementRules.ToUtilityKind(placement.Tool);
        bool previous = UtilityPresent(sim, kind, target);
        SimulationCommandResult result = sim.ExecuteCommand(
            new SetUtilityCommand(target, kind, Present: !remove));
        if (result.Success && previous == remove)
            _placementUndo.Push(new UtilityUndo(target, kind, previous));
        _ui.SetCommandStatus(result.Message);
        if (UiDebug)
            Console.WriteLine($"[UI] Utility: {result.Message}");
    }

    private void PlaceMachine(SimulationState sim, PlacementState placement, GridCoord target)
    {
        if (!PlacementRules.CanApply(sim, placement, target))
        {
            _ui.SetCommandStatus($"Cannot build {placement.Label} at {target}; choose an empty floor tile.");
            return;
        }

        BuildOption option = BuildCatalog.Get(placement.MachineType);
        SimulationCommandResult result = sim.ExecuteCommand(new InstallMachineCommand(
            target,
            option.Type,
            option.SteelPlates,
            option.MachineComponents,
            option.WorkSeconds));
        _ui.SetCommandStatus(result.Message);
        if (UiDebug)
            Console.WriteLine($"[UI] Machine: {result.Message}");
        if (result.Success)
            placement.Cancel();
    }

    private void UndoLastUtility(SimulationState sim)
    {
        if (_placementUndo.Count == 0)
        {
            _ui.SetCommandStatus("Nothing to undo.");
            return;
        }

        UtilityUndo edit = _placementUndo.Pop();
        SimulationCommandResult result = sim.ExecuteCommand(
            new SetUtilityCommand(edit.Target, edit.Utility, edit.PreviousPresent));
        _ui.SetCommandStatus("Undo: " + result.Message);
        if (UiDebug)
            Console.WriteLine($"[UI] Undo: {result.Message}");
    }

    private static bool UtilityPresent(SimulationState sim, UtilityKind kind, GridCoord target) => kind switch
    {
        UtilityKind.PowerCable => sim.Cables.Contains(target),
        UtilityKind.CoolantPipe => sim.PlumbingSolver.CoolantPipes.Contains(target),
        UtilityKind.PropellantPipe => sim.PlumbingSolver.PropellantPipes.Contains(target),
        _ => false
    };

    private static uint GetPlacementColor(PlacementState placement, bool valid, bool removing)
    {
        if (!valid)
            return 0x66FF3030;
        if (removing)
            return 0x66FF7040;

        return placement.Tool switch
        {
            PlacementTool.PowerCable => 0x6620C0FF,
            PlacementTool.CoolantPipe => 0x66FFA020,
            PlacementTool.PropellantPipe => 0x662080FF,
            PlacementTool.Machine => 0x6620FF80,
            _ => 0x66FFFFFF
        };
    }

    private static unsafe IntPtr ResolveGlFunction(byte* name)
        => Glfw.GetProcAddress(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(name));
}
