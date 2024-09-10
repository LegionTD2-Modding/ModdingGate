/*
    AutoHackGame
    Copyright (C) 2024  Alexandre 'kidev' Poumaroux

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using HarmonyLib;
using System;

namespace AutoHackGame;

public static class HudApi
{
    private static readonly Lazy<Traverse> HudApiTraverse = new Lazy<Traverse>(() => 
        Traverse.Create(AccessTools.TypeByName("Assets.Api.HudApi")));

    public static void TriggerHudEvent(string eventName)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string)])
            .GetValue(eventName);
    }

    public static void TriggerHudEvent(string eventName, string arg)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(string)])
            .GetValue(eventName, arg);
    }

    public static void TriggerHudEvent(string eventName, string arg1, string arg2)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(string), typeof(string)])
            .GetValue(eventName, arg1, arg2);
    }

    public static void TriggerHudEvent(string eventName, bool arg)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(bool)])
            .GetValue(eventName, arg);
    }

    public static void TriggerHudEvent(string eventName, int arg1)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(int)])
            .GetValue(eventName, arg1);
    }

    public static void TriggerHudEvent(string eventName, float arg1)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(float)])
            .GetValue(eventName, arg1);
    }

    public static void TriggerHudEvent(string eventName, float arg1, float arg2)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(float), typeof(float)])
            .GetValue(eventName, arg1, arg2);
    }

    public static void TriggerHudEvent(string eventName, int arg1, int arg2)
    {
        HudApiTraverse.Value.Method("TriggerHudEvent", [typeof(string), typeof(int), typeof(int)])
            .GetValue(eventName, arg1, arg2);
    }
}

