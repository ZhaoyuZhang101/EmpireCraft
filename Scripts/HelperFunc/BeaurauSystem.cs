using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;
public static class BeaurauSystem
{
    static string FILE_PATH = "Scripts/Data/OfficeData.json";
    static Dictionary<string,Office> lookup;
    public static Office empireOffice;
    public static void init()
    {
        readFromJson();
    }

    public static void readFromJson()
    {
        string whole_path = Path.Combine(ModClass._declare.FolderPath, FILE_PATH);
        string content = File.ReadAllText(whole_path);
        List<Office> offices = JsonConvert.DeserializeObject<List<Office>>(content);
        lookup = offices.ToDictionary(o => o.Id);
        var roots = new List<Office>();
        foreach (Office office in offices) 
        {
            if (!string.IsNullOrEmpty(office.ParentId) && lookup.TryGetValue(office.ParentId, out var parent))
            {
                parent.Children.Add(office);
                office.Parent = parent;
            }
            else
            {
                roots.Add(office);
            }
        }
        empireOffice = roots[0];
        PrintTree(roots);
    }

    public static void adjust(Empire empire, Office office)
    {
        foreach(Kingdom kingdom in empire.kingdoms_hashset)
        {

        }
    }

    public static void PrintTree(IEnumerable<Office> nodes, int depth = 0)
    {
        foreach (var node in nodes)
        {
            LogService.LogInfo($"{new string(' ', depth * 2)}- {node.Name} ({node.Leader})");
            PrintTree(node.Children, depth + 1);
        }
    }
}

public class Peerage
{
    public string Name { get; set; }
    public string OfficeId { get; set; }
    public PeerageType peerageType {  get; set; }
    public PeeragesLevel peerage_level { get; set; }
    public string 勋 { get; set; }
}

public class Office
{
    public string Id;
    public string Name { get; set; }
    [DefaultValue(BeaurauLevel.无)]
    public BeaurauLevel? Level { get; set; }
    [DefaultValue(官职.无)]
    public 官职 Leader { get; set; }
    public List<官职> SecondLeader { get; set; }
    public string Description { get; set; }
    public string IconPath { get; set; }
    public string ParentId { get; set; }
    public List<string> ChildrenIds { get; set; } = new List<string>();
    public int Index { get; set; }

    public Office Parent { get; set; }
    public List<Office> Children { get; set; } = new List<Office>();
    [JsonIgnore]
    public Transform Transform { get; set; }

    [JsonIgnore] public float subtreeWidth;
    [JsonIgnore] public Vector2 position;
}