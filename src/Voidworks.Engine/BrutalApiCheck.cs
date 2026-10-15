using System;
using System.Reflection;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Voidworks.Engine;

public static class BrutalApiCheck
{
    public static void PrintApiSummary()
    {
        Console.WriteLine("=== GLFW API Methods ===");
        foreach (var m in typeof(Glfw).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name.StartsWith("Create") || m.Name.StartsWith("Init") || m.Name.StartsWith("Poll") || 
                m.Name.StartsWith("Set") || m.Name.StartsWith("Get") || m.Name.StartsWith("Swap"))
            {
                Console.WriteLine($"  {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
            }
        }

        Console.WriteLine("\n=== IMGUI API Methods ===");
        var imguiMethods = typeof(ImGui).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Begin") || m.Name.StartsWith("Text") || m.Name.StartsWith("Button") || m.Name.StartsWith("NewFrame") || m.Name.StartsWith("Render"))
            .Select(m => m.Name).Distinct().Take(20);
        foreach (var name in imguiMethods)
        {
            Console.WriteLine($"  ImGui.{name}");
        }
    }
}
