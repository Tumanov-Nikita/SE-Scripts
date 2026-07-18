using ParallelTasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game.Utils;
using VRage.Game.VisualScripting;
using VRage.Scripting;
using VRageMath;
using static Spotter.Program;
using static Sandbox.Game.World.MyWorldGenerator;
using static System.BitStreamExtensions;
using static System.Net.WebRequestMethods;
using static System.Reflection.Metadata.BlobBuilder;
using static VRage.Game.MyObjectBuilder_ControllerSchemaDefinition;

namespace Spotter
{
    public sealed class Program : MyGridProgram
    {







        IMyTextPanel LCD;
        MyDetectedEntityInfo Target;

        #region Настройки
        public readonly double ScanRange = 10000;
        #endregion

        #region Переменные для наименований блоков и групп блоков

        public readonly string CameraName = "Камера Споттер";
        public readonly string LCDName = "Экран Споттер";

        #endregion


        private static Program myScript;
        SpottingHandler spottingHandler;
        int coordsCount = 0;

        public Program()
        {
            myScript = this;
            

            LCD = GridTerminalSystem.GetBlockWithName(LCDName) as IMyTextPanel;

            spottingHandler = new SpottingHandler(LCD);

            Runtime.UpdateFrequency = UpdateFrequency.Update100;


        }

        /// <summary>
        /// Запуск программного блока, выбор стартового режима
        /// </summary>
        /// <param name="arg">Аргумент запуска</param>
        public void Main(string arg)
        {
            switch (arg)
            {
                case "Scan":
                    if (spottingHandler.Scan(ScanRange))
                    {
                        spottingHandler.PrintVector((Vector3D)myScript.Target.HitPosition, $"Таргет {myScript.coordsCount.ToString()}", true);
                        myScript.coordsCount++;
                    }
                    break;
                case "Clear":
                    spottingHandler.Clear();
                    break;
                default:
                    break;

            }
        }

        


        public class SpottingHandler
        {
            private readonly IMyTextPanel _lcd;
            private readonly IMyCameraBlock Camera;


            public SpottingHandler(IMyTextPanel lcd)
            {
                _lcd = lcd;
                Camera = myScript.GridTerminalSystem.GetBlockWithName(myScript.CameraName) as IMyCameraBlock;
                Camera.EnableRaycast = true;
            }

            public bool Scan(double range)
            {
                myScript.Target = Camera.Raycast(range, 0, 0);
                return !myScript.Target.IsEmpty();
            }

            public void Clear()
            {
                _lcd.WriteText("");
            }

            public void PrintVector(Vector3D vector, string name, bool append, string colorHEX = "#FF00FF")
            {
                _lcd.WriteText($"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n", append);
            }

        }

        













    }
}