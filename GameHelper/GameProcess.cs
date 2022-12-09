﻿// <copyright file="GameProcess.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using Coroutine;
    using CoroutineEvents;
    using GameOffsets;
    using ImGuiNET;
    using SixLabors.ImageSharp;
    using Utils;

    /// <summary>
    ///     Allows process manipulation. It uses the (time/event based) co-routines
    ///     to continuously monitor and open a process with the specific name. It exposes
    ///     variables/events for the caller to use.
    ///     Base class OnControllerReady is only triggered when all static addresses are found.
    ///     Limitation: This class will not open a game process if multiple processes match
    ///     the name because it does not know which process to select.
    /// </summary>
    public class GameProcess
    {
        private readonly List<Process> processesInfo = new();
        private int clientSelected = -1;
        private bool showSelectGameMenu = false;
        private bool closeForcefully = false;

        /// <summary>
        ///     Initializes a new instance of the <see cref="GameProcess" /> class.
        /// </summary>
        internal GameProcess()
        {
            CoroutineHandler.Start(this.FindAndOpen());
            CoroutineHandler.Start(this.FindStaticAddresses());
            CoroutineHandler.Start(this.AskUserToSelectClient());
        }

        /// <summary>
        ///     Gets the Pid of the game or zero in case game isn't running..
        /// </summary>
        public uint Pid
        {
            get
            {
                try
                {
                    return (uint)this.Information.Id;
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>
        ///     Gets a value indicating whether the game is foreground or not.
        /// </summary>
        public bool Foreground { get; private set; }

        /// <summary>
        ///     Gets the game size and position with respect to the monitor screen.
        /// </summary>
        public Rectangle WindowArea { get; private set; } = Rectangle.Empty;

        /// <summary>
        ///     Gets the Base Address of the game.
        /// </summary>
        internal IntPtr Address
        {
            get
            {
                try
                {
                    var reader = this.Handle;
                    if (reader != null && !reader.IsClosed && !reader.IsInvalid)
                    {
                        return this.Information.MainModule.BaseAddress;
                    }

                    return IntPtr.Zero;
                }
                catch (Exception)
                {
                    return IntPtr.Zero;
                }
            }

            private set { }
        }

        /// <summary>
        ///     Gets the event which is triggered when GameProcess
        ///     has found all the static offset patterns.
        /// </summary>
        internal Event OnStaticAddressFound { get; } = new();

        /// <summary>
        ///     Gets the static addresses (along with their names) found in the GameProcess
        ///     based on the GameOffsets.StaticOffsets file.
        /// </summary>
        internal Dictionary<string, IntPtr> StaticAddresses { get; } =
            new();

        /// <summary>
        ///     Gets the game diagnostics information.
        /// </summary>
        internal Process Information { get; private set; }

        /// <summary>
        ///     Gets the game handle.
        /// </summary>
        internal Memory Handle { get; private set; }

        /// <summary>
        ///     Closes the handle for the game and releases all the resources.
        /// </summary>
        /// <param name="monitorForNewGame">
        ///     Set to true if caller wants to start monitoring for new game process after closing.
        /// </param>
        internal void Close(bool monitorForNewGame = true)
        {
            CoroutineHandler.RaiseEvent(GameHelperEvents.OnClose);
            this.WindowArea = Rectangle.Empty;
            this.Foreground = false;
            this.Handle?.Dispose();
            this.Information?.Close();
            if (monitorForNewGame)
            {
                CoroutineHandler.Start(this.FindAndOpen());
            }
        }

        /// <summary>
        ///     Finds the list of processes from the list of processes running on the system
        ///     based on the GameOffsets.GameProcessName class.
        /// </summary>
        /// <returns>
        ///     co-routine IWait.
        /// </returns>
        private IEnumerator<Wait> FindAndOpen()
        {
            while (true)
            {
                yield return new Wait(2d);
                this.processesInfo.Clear();
                foreach (var process in Process.GetProcesses())
                {
                    if (GameProcessDetails.ProcessName.TryGetValue(process.ProcessName, out var windowTitle))
                    {
                        if (process.MainWindowTitle.ToLower() == windowTitle)
                        {
                            this.processesInfo.Add(process);
                        }
                    }
                }

                if (this.processesInfo.Count == 1)
                {
                    this.Information = this.processesInfo[0];
                    if (this.Open())
                    {
                        break;
                    }
                }
                else if (this.processesInfo.Count > 1)
                {
                    this.ShowSelectGameMenu();
                    if (this.clientSelected > -1 && this.clientSelected < this.processesInfo.Count)
                    {
                        this.Information = this.processesInfo[this.clientSelected];
                        if (this.Open())
                        {
                            break;
                        }
                    }
                }
            }
        }

        private IEnumerator<Wait> AskUserToSelectClient()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (this.showSelectGameMenu)
                {
                    ImGui.OpenPopup("SelectGameMenu");
                }

                if (ImGui.BeginPopup("SelectGameMenu"))
                {
                    for (var i = 0; i < this.processesInfo.Count; i++)
                    {
                        var foreground = GetForegroundWindow() == this.processesInfo[i].MainWindowHandle;
                        if (ImGui.RadioButton($"{i} - PathOfExile - Focused: {foreground}", i == this.clientSelected))
                        {
                            this.clientSelected = i;
                        }
                    }

                    ImGui.BeginDisabled(this.Address == IntPtr.Zero);
                    if (ImGui.Button("Done"))
                    {
                        this.HideSelectGameMenu();
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndDisabled();
                    ImGui.SameLine();
                    if (ImGui.Button("Retry or Delay Selection"))
                    {
                        this.HideSelectGameMenu();
                        ImGui.CloseCurrentPopup();
                        this.closeForcefully = true;
                    }

                    ImGui.EndPopup();
                }
            }
        }

        private void HideSelectGameMenu()
        {
            this.clientSelected = -1;
            this.processesInfo.Clear();
            this.showSelectGameMenu = false;
        }

        private void ShowSelectGameMenu()
        {
            this.showSelectGameMenu = true;
        }

        /// <summary>
        ///     Monitors the game process for changes.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private IEnumerator<Wait> Monitor()
        {
            while (true)
            {
                // Have to check MainWindowHandle because
                // sometime HasExited returns false even when game isn't running..
                if (this.Information.HasExited ||
                    this.Information.MainWindowHandle.ToInt64() <= 0x00 ||
                    this.closeForcefully)
                {
                    this.closeForcefully = false;
                    this.Close();
                    break;
                }

                this.UpdateIsForeground();
                this.UpdateWindowRectangle();

                yield return new Wait(1d);
            }
        }

        /// <summary>
        ///     Finds the static addresses in the GameProcess based on the
        ///     GameOffsets.StaticOffsetsPatterns file.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private IEnumerator<Wait> FindStaticAddresses()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnOpened);
                var baseAddress = this.Address;
                if (baseAddress == IntPtr.Zero)
                {
                    continue;
                }

                var procSize = this.Information.MainModule.ModuleMemorySize;
                var patternsInfo = PatternFinder.Find(this.Handle, baseAddress, procSize);
                foreach (var pi in patternsInfo)
                {
                    //if (pi.Key == "GameWindowScaleValues") {
                    //    var start = baseAddress + pi.Value + 1 + 12;//12 befor line i need(with addres i need)
                    //    var skip = this.Handle.ReadMemory<int>(start + 4); // 4 byte opcodes befor address i need
                    //    var res = start + skip + 8; //8 coz instruction here 4(opcode)+4(address)
                    //    this.StaticAddresses[pi.Key] = res;
                    //    continue;
                    //}
                    var offsetDataValue = this.Handle.ReadMemory<int>(baseAddress + pi.Value);
                    var address = baseAddress + pi.Value + offsetDataValue + 0x04;
                    this.StaticAddresses[pi.Key] = address;
                }

                CoroutineHandler.RaiseEvent(this.OnStaticAddressFound);
            }
        }

        /// <summary>
        ///     Opens the handle for the game process.
        /// </summary>
        private bool Open()
        {
            this.Handle = new Memory(this.Information.Id);
            if (this.Handle.IsInvalid)
            {
                return false;
            }

            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(this.Monitor(), "[GameProcess] Monitoring Game Process"));
            CoroutineHandler.RaiseEvent(GameHelperEvents.OnOpened);
            return true;
        }

        /// <summary>
        ///     Updates the Foreground Property of the GameProcess class.
        /// </summary>
        private void UpdateIsForeground()
        {
            var foreground = GetForegroundWindow() == this.Information.MainWindowHandle;
            if (foreground != this.Foreground)
            {
                this.Foreground = foreground;
                CoroutineHandler.RaiseEvent(GameHelperEvents.OnForegroundChanged);
            }
        }

        /// <summary>
        ///     Gets the game process window area with reference to the monitor screen.
        /// </summary>
        private void UpdateWindowRectangle()
        {
            GetClientRect(this.Information.MainWindowHandle, out var size);
            ClientToScreen(this.Information.MainWindowHandle, out var pos);
            var sizePos = size.ToRectangle(pos);
            if (sizePos != this.WindowArea && sizePos.Size != Size.Empty)
            {
                this.WindowArea = sizePos;
                CoroutineHandler.RaiseEvent(GameHelperEvents.OnMoved);
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, out Point lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            private readonly int left;
            private readonly int top;
            private readonly int right;
            private readonly int bottom;

            internal Rectangle ToRectangle(Point point)
            {
                return new Rectangle(point.X, point.Y, this.right - this.left, this.bottom - this.top);
            }
        }
    }
}