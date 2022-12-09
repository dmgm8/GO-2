﻿// <copyright file="InGameState.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using CoroutineEvents;
    using GameOffsets.Objects.States;
    using InGameStateObjects;
    using UiElement;

    /// <summary>
    ///    [2].
    /// </summary>
    public class InGameState : RemoteObjectBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="InGameState" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal InGameState(IntPtr address)
            : base(address)
        {
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnPerFrame(), "[InGameState] Update Game State", int.MaxValue - 2));
        }

        /// <summary>
        ///     Gets the data related to the currently loaded world area.
        /// </summary>
        public WorldData CurrentWorldInstance { get; }

            = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the data related to the current area instance.
        /// </summary>
        public AreaInstance CurrentAreaInstance { get; }

            = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the UiRoot main child which contains all the UiElements of the game.
        /// </summary>
        public ImportantUiElements GameUi { get; }

            = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the data related to the root ui element.
        /// </summary>
        internal UiElementBase UiRoot { get; }

            = new(IntPtr.Zero);

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.CurrentAreaInstance.Address = IntPtr.Zero;
            this.UiRoot.Address = IntPtr.Zero;
            this.GameUi.Address = IntPtr.Zero;
            this.CurrentWorldInstance.Address = IntPtr.Zero;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<InGameStateOffset>(this.Address);

            this.CurrentWorldInstance.Address = data.WorldData;
            this.CurrentAreaInstance.Address = data.AreaInstanceData;
            this.UiRoot.Address = data.UiRootPtr;
            this.GameUi.Address = data.IngameUi;
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            // TODO optimization: convert this into OnAreaChange.
            while (true)
            {
                yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
                if (this.Address != IntPtr.Zero)
                {
                    this.UpdateData(false);
                }
            }
        }
    }
}