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
using VRage.Game.VisualScripting;
using VRage.Scripting;
using VRageMath;
using static Sandbox.Game.World.MyWorldGenerator;
using static System.Net.WebRequestMethods;
using static System.Reflection.Metadata.BlobBuilder;

namespace PlanetaryBallisticRocket
{
    public sealed class Program : MyGridProgram

    {



       

        #region Настройки
        

        #endregion

        #region Переменные для наименований блоков и групп блоков
        
        #endregion

        private char CurrentIcon;
        private string CurrentStatus = "";
        private static Program myScript;
        MiningHandler miningHandler;


        public Program()
        {
            myScript = this;
            miningHandler = new MiningHandler();

            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        /// <summary>
        /// Запуск программного блока, выбор стартового режима
        /// </summary>
        /// <param name="arg">Аргумент запуска</param>
        public void Main(string arg)
        {
            switch (arg)
            {
                case "mining":
                    CurrentStatus = "mining";
                    break;
                default:
                    IconSpin();
                    myScript.Echo("Current status is " + CurrentStatus);
                    HandleStatus();
                    break;
            }
        }

        /// <summary>
        /// Обработка текущего статуса
        /// </summary>
        private void HandleStatus()
        {
            switch (CurrentStatus)
            {
                
                default:
                    break;
            }
        }

        /// <summary>
        /// Косметическая крутилка
        /// </summary>
        private void IconSpin()
        {
            switch (CurrentIcon)
            {
                case '–':
                    CurrentIcon = '\\';
                    break;
                case '\\':
                    CurrentIcon = '/';
                    break;
                case '/':
                    CurrentIcon = '–';
                    break;
                default:
                    CurrentIcon = '–';
                    break;
            }
            myScript.Echo(CurrentIcon.ToString());
        }
        public class MiningHandler
        {
            #region Объявление переменных
            

            #endregion

            public MiningHandler()
            {
                #region Начальная инициализация

                

                #endregion
            }

            #region Обработка статусов


            #endregion

        }















    }
}