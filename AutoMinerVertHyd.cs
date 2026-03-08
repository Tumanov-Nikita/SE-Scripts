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

public sealed class Program : MyGridProgram

{



    /*
     * Минимальная конфигурация дрона 
     * (Водородный, вертикальный, с батареями в качестве основной энергии):
     * Блок дистанционного управления х1
     * Программный блок х1
     * Коннектор х1
     * Полет ИИ х1
     * Регистратор событий ИИ х2
     * Таймер х3
     * 
     */

    #region Настройки
    /// <summary>
    /// Количество шахт квадратно-гнездового метода в ширину
    /// </summary>
    public byte ShaftM = 3;
    /// <summary>
    /// Количество шахт квадратно-гнездового метода в длину
    /// </summary>
    public byte ShaftN = 3;
    /// <summary>
    /// Порог заполнения хранилищ, в %
    /// </summary>
    public float StorageCapacityThreshold = 75;
    /// <summary>
    /// Порог заряда батарей, в %
    /// </summary>
    public float BatteriesCapacityThreshold = 10;
    /// <summary>
    /// Порог заполнения водородных баков, в %
    /// </summary>
    public float TanksCapacityThreshold = 20;
    /// <summary>
    /// Максимальная высота над уровнем моря для дуги перемещения, в метрах
    /// </summary>
    public float ArcHeightMaximum = 10000;
    /// <summary>
    /// Мультипликатор для сигнала гироскопам
    /// </summary>
    public float GyroMult = 5;
    /// <summary>
    /// Ограничение скорости для перемещения на поверхности, в м/c
    /// </summary>
    public float SurfaceSpeedLimit = 200;
    /// <summary>
    /// Ограничение скорости для перемещения в шахте, в м/c
    /// </summary>
    public float MiningSpeedLimit = 0.25f;
    /// <summary>
    /// Максимально допустимая точность для совмещения с точкой назначения, в м
    /// </summary>
    public float AcceptableMovingAccuracy = 0.5f;
    /// <summary>
    /// Дополнительный отступ от ширины/длины дрона для разметки шахт, в м
    /// </summary>
    public float MiningMargin = 1.6f;
    /// <summary>
    /// Мультипликатор высоты для дуги перемещения, рекомендуется от 0.3 до 1
    /// </summary>
    public float ArcHeightMult = 0.75f;
    #endregion

    #region Переменные для наименований блоков и групп блоков
    public string EventControllerName = "Контроллер события ИИ Выгрузка";
    public string FlightControllerName = "Полет ИИ Майнер";
    public string RemoteControllerName = "ДУ ИИ Майнер";
    public string TimerToBaseName = "Таймер к Базе для ИИ Майнер";
    public string TimerFromBaseName = "Таймер от Базы для ИИ Майнер";
    public string StoragesGroupName = "Контейнеры Майнер";
    public string ConnectorGroupName = "Коннекторы Майнер";
    public string BatteriesGroupName = "Батареи Майнер";
    public string TanksGroupName = "Баки Майнер";
    public string DrillsGroupName = "Буры Майнер";
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
            case "returnToBase":
                CurrentStatus = "returnToBase";
                break;
            case "parkingToBase":
                CurrentStatus = "parkingToBase";
                break;
            case "exitFromBase":
                CurrentStatus = "exitFromBase";
                break;
            case "movingToMine":
                CurrentStatus = "movingToMine";
                break;
            case "movingToCurrentShaft":
                CurrentStatus = "movingToCurrentShaft";
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
            case "mining":
                miningHandler.Mining();
                break;
            case "returnToBase":
                miningHandler.ReturningToBase();
                break;
            case "parkingToBase":
                miningHandler.ParkingToBase();
                break;
            case "exitFromBase":
                miningHandler.ExitFromBase();
                break;
            case "movingToMine":
                miningHandler.MovingToMine();
                break;
            case "movingToCurrentShaft":
                miningHandler.MovingToCurrentShaft();
                break;
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
        private readonly IMyFlightMovementBlock flightMovement;
        private readonly IMyEventControllerBlock eventController;
        private readonly IMyRemoteControl remoteControl;
        private readonly IMyTimerBlock timerForAIToBase, timerForAIFromBase;
        private readonly List<IMyInventoryOwner> storages;
        private readonly List<IMyBatteryBlock> batteries;
        private readonly List<IMyGasTank> tanks;
        private readonly List<IMyShipDrill> drills;
        private readonly List<IMyGyro> gyros;
        private readonly List<IMyShipConnector> connectors;
        private readonly IMyBlockGroup storagesGroup, batteriesGroup, tanksGroup, drillsGroup, connectorsGroup;
        private readonly List<IMyThrust> thrForward = new List<IMyThrust>();
        private readonly List<IMyThrust> thrBackward = new List<IMyThrust>();
        private readonly List<IMyThrust> thrRight = new List<IMyThrust>();
        private readonly List<IMyThrust> thrLeft = new List<IMyThrust>();
        private readonly List<IMyThrust> thrUp = new List<IMyThrust>();
        private readonly List<IMyThrust> thrDown = new List<IMyThrust>();
        private readonly double forwardThrustEff = 0;
        private readonly double backwardThrustEff = 0;
        private readonly double rightThrustEff = 0;
        private readonly double leftThrustEff = 0;
        private readonly double upThrustEff = 0;
        private readonly double downThrustEff = 0;
        private bool IsMiningComplete;
        private bool IsGridHorizontallyAligned;
        private Vector3D ForwardVector;
        private Vector3D PlanetCenter;
        private Vector3D MineCenterPosition;
        private Vector3D CurrentMiningPosition;
        private Vector3D BasePosition;
        private List<ShaftMark> shaftMarks = new List<ShaftMark>();
        private Vector3D arcStartPos;
        private Vector3D arcTargetPos;
        private Vector3D arcApexPos;
        private Vector3D arcPlaneNormal;
        private bool arcInitialized = false;
        private bool arcIsAscending = true;
        Vector3D sizeInMeters;


        private readonly IMyProgrammableBlock pb;
        private readonly IMyTextPanel textPanel;
        private readonly IMyTextSurface LCD;
        private double maxSpeed = 0;
        #endregion

        public MiningHandler()
        {
            #region Начальная инициализация

            pb = (IMyProgrammableBlock)myScript.GridTerminalSystem.GetBlockWithName("Программируемый блок ии");
            LCD = pb.GetSurface(0);
            textPanel = (IMyTextPanel)myScript.GridTerminalSystem.GetBlockWithName("LCD panel");


            flightMovement = (IMyFlightMovementBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.FlightControllerName);
            eventController = (IMyEventControllerBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.EventControllerName);
            remoteControl = (IMyRemoteControl)myScript.GridTerminalSystem.GetBlockWithName(myScript.RemoteControllerName);
            timerForAIToBase = (IMyTimerBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.TimerToBaseName);
            timerForAIFromBase = (IMyTimerBlock)myScript.GridTerminalSystem.GetBlockWithName(myScript.TimerFromBaseName);
            remoteControl.ControlThrusters = true;
            MineCenterPosition = new Vector3D(0);
            CurrentMiningPosition = new Vector3D(0);
            BasePosition = new Vector3D(0);
            IsMiningComplete = false;
            Vector3I sizeInBlocks = remoteControl.CubeGrid.Max - remoteControl.CubeGrid.Min + new Vector3I(1, 1, 1);
            sizeInMeters = new Vector3D(sizeInBlocks) * remoteControl.CubeGrid.GridSize;

            gyros = new List<IMyGyro>();
            myScript.GridTerminalSystem.GetBlocksOfType<IMyGyro>(gyros);
            connectors = new List<IMyShipConnector>();
            connectorsGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.ConnectorGroupName);
            connectorsGroup.GetBlocksOfType(connectors);
            storages = new List<IMyInventoryOwner>();
            storagesGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.StoragesGroupName);
            storagesGroup.GetBlocksOfType(storages);
            batteries = new List<IMyBatteryBlock>();
            batteriesGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.BatteriesGroupName);
            batteriesGroup.GetBlocksOfType(batteries);
            tanks = new List<IMyGasTank>();
            tanksGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.TanksGroupName);
            tanksGroup.GetBlocksOfType(tanks);
            drills = new List<IMyShipDrill>();
            drillsGroup = myScript.GridTerminalSystem.GetBlockGroupWithName(myScript.DrillsGroupName);
            drillsGroup.GetBlocksOfType(drills);

            //Инциализация двигателей по направлениям
            Matrix RemConMatrix = new Matrix();
            remoteControl.Orientation.GetMatrix(out RemConMatrix);
            Matrix ThrMatrix = new Matrix();
            List<IMyThrust> ThrTemp = new List<IMyThrust>();
            myScript.GridTerminalSystem.GetBlocksOfType<IMyThrust>(ThrTemp);
            foreach (IMyThrust thr in ThrTemp)
            {
                thr.Orientation.GetMatrix(out ThrMatrix);
                if (ThrMatrix.Forward == RemConMatrix.Backward)
                {
                    thrForward.Add(thr);
                    forwardThrustEff += thr.MaxEffectiveThrust;
                }
                else if (ThrMatrix.Forward == RemConMatrix.Forward)
                {
                    thrBackward.Add(thr);
                    backwardThrustEff += thr.MaxEffectiveThrust;
                }
                else if (ThrMatrix.Forward == RemConMatrix.Left)
                {
                    thrRight.Add(thr);
                    rightThrustEff += thr.MaxEffectiveThrust;
                }
                else if (ThrMatrix.Forward == RemConMatrix.Right)
                {
                    thrLeft.Add(thr);
                    leftThrustEff += thr.MaxEffectiveThrust;
                }
                else if (ThrMatrix.Forward == RemConMatrix.Down)
                {
                    thrUp.Add(thr);
                    upThrustEff += thr.MaxEffectiveThrust;
                }
                else if (ThrMatrix.Forward == RemConMatrix.Up)
                {
                    thrDown.Add(thr);
                    downThrustEff += thr.MaxEffectiveThrust;
                }
            }

            remoteControl.TryGetPlanetPosition(out PlanetCenter);

            #endregion
        }

        #region Обработка статусов

        /// <summary>
        /// Режим выкапывания шахты
        /// </summary>
        public void Mining()
        {
            KeepStraightDirection();
            if (CheckStorageAndTanksAndBatteries())
            {
                if (IsGridHorizontallyAligned)
                {
                    SetDrillsEnabled(true);

                    var currentValidShaft = GetCurrentShaft();
                    if (!currentValidShaft.endCoords.IsZero())
                    {
                        CurrentMiningPosition = currentValidShaft.endCoords;
                    }

                    var speedLimit = myScript.MiningSpeedLimit;
                    //double elevationSurface;
                    //remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevationSurface);
                    //if (elevationSurface - sizeInMeters.Y > 2) // Увеличение скорости, если находимся больше, чем в 2 метрах над поверхностью
                    //{
                    //    speedLimit *= 10;
                    //}
                    Vector3D linearVelocity = remoteControl.GetShipVelocities().LinearVelocity;
                    if (linearVelocity.Length() > maxSpeed)
                    {
                        maxSpeed = linearVelocity.Length();
                        textPanel.WriteText($"maxSpeed = {maxSpeed}");
                    }

                    if (MovementOnVectorLinear(CurrentMiningPosition, speedLimit, true))
                    {
                        currentValidShaft.isFinished = true;
                        CurrentMiningPosition = currentValidShaft.startCoords;

                        SetDrillsEnabled(false);
                        SetGyrosOverride(false);
                        StopAllGyros();
                        StopAllThrusters();
                        SetStatus("movingToCurrentShaft");
                    }
                }
            }
            else
            {
                var currentValidShaft = GetCurrentShaft();
                if (!currentValidShaft.startCoords.IsZero())
                {
                    CurrentMiningPosition = currentValidShaft.startCoords;
                }
                SetDrillsEnabled(false);
                SetGyrosOverride(false);
                StopAllGyros();
                StopAllThrusters();
                SetStatus("movingToCurrentShaft");
            }
        }
        /// <summary>
        /// Режим возврата на базу
        /// </summary>
        internal void ReturningToBase()
        {
            if (BasePosition.IsZero())
            {
                SetGyrosOverride(false);
                StopAllGyros();
                StopAllThrusters();
                myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
            }

            SetGyrosOverride(true);

            if (MovementOnVectorArchwise(BasePosition, myScript.SurfaceSpeedLimit))
            {
                SetGyrosOverride(false);
                StopAllGyros();
                StopAllThrusters();
                SetStatus("parkingToBase");
            }
        }
        /// <summary>
        /// Режим парковки на базе
        /// </summary>
        internal void ParkingToBase()
        {
            if (!flightMovement.Enabled)
            {
                flightMovement.Enabled = true;
                flightMovement.AlignToPGravity = true;
                SetConnectorsEnabled(true);
                timerForAIToBase.Trigger();
            }
            if (CheckStorageAndTanksAndBatteries() && CheckStoragesInPercent() == 0 
                && CheckTanksInPercent() > myScript.TanksCapacityThreshold * 4 
                && flightMovement.Enabled)
            {
                flightMovement.Enabled = false;
                SetStatus("exitFromBase");
            }
        }
        /// <summary>
        /// Режим выхода с базы
        /// </summary>
        internal void ExitFromBase()
        {
            if (IsMiningComplete)
            {
                SetStatus("Mining complete!");
                myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
            }
            else if (!flightMovement.Enabled)
            {
                SetBatteriesRecharge(false);
                SetTanksStockpile(false);
                SetConnectorsEnabled(false);
                flightMovement.Enabled = true;
                flightMovement.AlignToPGravity = true;
                timerForAIFromBase.Trigger();
            }

            if (!flightMovement.IsAutoPilotEnabled)
            {
                if (BasePosition.IsZero())
                {
                    BasePosition = remoteControl.GetPosition();
                }
                if (ForwardVector.IsZero())
                {
                    ForwardVector = Vector3D.Normalize(Vector3D.Reject(
                        remoteControl.WorldMatrix.Forward, 
                        Vector3D.Normalize(remoteControl.GetNaturalGravity())));
                }
                flightMovement.Enabled = false;
                SetConnectorsEnabled(true);
                SetStatus("movingToMine");
            }
        }
        /// <summary>
        /// Режим перемещения к месту раскопок
        /// </summary>
        internal void MovingToMine()
        {
            if (CheckStorageAndTanksAndBatteries())
            {
                var currentValidShaft = GetCurrentShaft();
                if (currentValidShaft.startCoords.IsZero())
                {
                    if (CurrentMiningPosition.IsZero())
                    {
                        if (MineCenterPosition.IsZero())
                        {
                            List<MyWaypointInfo> myWaypoints = new List<MyWaypointInfo>();
                            remoteControl.GetWaypointInfo(myWaypoints);
                            if (myWaypoints.Count > 0)
                            {
                                MineCenterPosition = myWaypoints[0].Coords;
                            }
                            else
                            {
                                myScript.Runtime.UpdateFrequency = UpdateFrequency.None;
                            }
                        }
                        else
                        {
                            CurrentMiningPosition = MineCenterPosition;
                        }
                    }
                }
                else
                {
                    CurrentMiningPosition = currentValidShaft.startCoords;
                }

                if (MovementOnVectorArchwise(CurrentMiningPosition, myScript.SurfaceSpeedLimit))
                {
                    SetGyrosOverride(false);
                    SetStatus("movingToCurrentShaft");
                }
            }
            else
            {
                SetGyrosOverride(false);
                StopAllGyros();
                StopAllThrusters();
                SetStatus("returnToBase");
            }
        }
        /// <summary>
        /// Режим перемещения к старту текущей шахты
        /// </summary>
        internal void MovingToCurrentShaft()
        {
            KeepStraightDirection();
            if (CheckStorageAndTanksAndBatteries())
            {
                if (IsGridHorizontallyAligned)
                {
                    if (MineCenterPosition.IsZero())
                    {
                        List<MyWaypointInfo> myWaypoints = new List<MyWaypointInfo>();
                        remoteControl.GetWaypointInfo(myWaypoints);
                        if (myWaypoints.Count > 0)
                        {
                            MineCenterPosition = myWaypoints[0].Coords;
                        }
                        else
                        {
                            MineCenterPosition = remoteControl.GetPosition();
                        }
                    }

                    if (shaftMarks.Count == 0)
                    {
                        if (ForwardVector.IsZero())
                        {
                            ForwardVector = Vector3D.Normalize(Vector3D.Reject(
                                remoteControl.WorldMatrix.Forward,
                                Vector3D.Normalize(remoteControl.GetNaturalGravity())));
                        }
                        CreateShaftMarks(ref shaftMarks, MineCenterPosition, myScript.ShaftM, myScript.ShaftN);
                    }
                    var currentValidShaft = GetCurrentShaft();
                    if (!currentValidShaft.startCoords.IsZero())
                    {
                        CurrentMiningPosition = currentValidShaft.startCoords;
                    }
                    else if (MovementOnVectorLinear(CurrentMiningPosition, myScript.MiningSpeedLimit * 10, false))
                    {
                        IsMiningComplete = true;
                        SetGyrosOverride(false);
                        StopAllGyros();
                        StopAllThrusters();
                        SetStatus("returnToBase");
                    }
                    if (MovementOnVectorLinear(CurrentMiningPosition, myScript.MiningSpeedLimit * 10, false))
                    {
                        SetGyrosOverride(false);
                        StopAllGyros();
                        StopAllThrusters();
                        SetStatus("mining");
                    }
                    //PrintVector(CurrentMiningPosition, "currPos", false);
                }
            }
            else
            {
                var currentValidShaft = GetCurrentShaft();
                if (!currentValidShaft.startCoords.IsZero())
                {
                    CurrentMiningPosition = currentValidShaft.startCoords;
                }
                if (MovementOnVectorLinear(CurrentMiningPosition, myScript.MiningSpeedLimit * 10, false))
                {
                    SetGyrosOverride(false);
                    StopAllGyros();
                    StopAllThrusters();
                    SetStatus("returnToBase");
                }
            }
        }

        #endregion


        #region Методы для перемещения
        /// <summary>
        /// Выравнивает дрон по вектору планетарной гравитации, делает свой вектор Down сонаправленным ему
        /// </summary>
        private void KeepStraightDirection()
        {
            Vector3D gravVectorNorm = Vector3D.Normalize(remoteControl.GetNaturalGravity());
            Vector3D axisGrav = gravVectorNorm.Cross(remoteControl.WorldMatrix.Down);
            if (axisGrav.Dot(remoteControl.WorldMatrix.Down) < 0)
            {
                axisGrav = Vector3D.Normalize(axisGrav);
            }

            Vector3D axisForward = ForwardVector.Cross(remoteControl.WorldMatrix.Forward);
            if (ForwardVector.Dot(remoteControl.WorldMatrix.Forward) < 0)
            {
                axisForward = Vector3D.Normalize(axisForward);
            }

            float pitch = (float)axisGrav.Dot(remoteControl.WorldMatrix.Right);
            float roll = (float)axisGrav.Dot(remoteControl.WorldMatrix.Backward);
            float yaw = (float)axisForward.Dot(remoteControl.WorldMatrix.Up);

            //myScript.Echo($"axisGrav = {axisGrav:F}");
            //myScript.Echo($"axisGravDot = {axisGrav.Dot(remoteControl.WorldMatrix.Down):F}");
            //myScript.Echo($"pitch = {pitch:F}");
            //myScript.Echo($"roll = {roll:F}");

            foreach (IMyGyro gyro in gyros)
            {
                gyro.GyroOverride = true;
                gyro.Pitch = pitch * (myScript.GyroMult / 2);
                gyro.Roll = roll * (myScript.GyroMult / 2);
                gyro.Yaw = yaw * (myScript.GyroMult / 2);
            }
            IsGridHorizontallyAligned = axisGrav.Length() + axisForward.Length() < 0.01;
        }
        /// <summary>
        /// Устанавливает всем гироскопам значение перехвата управления
        /// </summary>
        /// <param name="overrideControls">Параметр, включающий или выключающий перехват управления</param>
        private void SetGyrosOverride(bool overrideControls)
        {
            foreach (IMyGyro gyro in gyros)
            {
                gyro.GyroOverride = overrideControls;
            }
        }
        /// <summary>
        /// Устанавливает всем гироскопам значение 0 по тангажу, рысканию и крену
        /// </summary>
        private void StopAllGyros()
        {
            foreach (IMyGyro gyro in gyros)
            {
                gyro.Pitch = 0;
                gyro.Roll = 0;
                gyro.Yaw = 0;
            }
        }
        /// <summary>
        /// Устанавливает процентное значение тяги двигателям
        /// </summary>
        /// <param name="list">Лист двигателей</param>
        /// <param name="value">Значение тяги (от 0 до 1)</param>
        private void SetTrustersPercentage(List<IMyThrust> list, float value)
        {
            foreach (IMyThrust thrust in list)
            {
                thrust.ThrustOverridePercentage = value;
            }
        }
        /// <summary>
        /// Постепенно увеличивает значение тяги двигателям
        /// </summary>
        /// <param name="list">Лист двигателей</param>
        private void SetTrustersGradually(List<IMyThrust> list)
        {
            var gravL = remoteControl.GetNaturalGravity().Length();
            textPanel.WriteText($"gravL = {gravL}\n", false);
            textPanel.WriteText($"gravL/400 = {gravL/400}\n", true);
            foreach (IMyThrust thrust in list)
            {
                thrust.ThrustOverridePercentage += 0.025f;
            }
        }
        /// <summary>
        /// Устанавливает значение тяги 0 всем двигателям
        /// </summary>
        private void StopAllThrusters()
        {
            SetTrustersPercentage(thrForward, 0);
            SetTrustersPercentage(thrBackward, 0);
            SetTrustersPercentage(thrRight, 0);
            SetTrustersPercentage(thrLeft, 0);
            SetTrustersPercentage(thrUp, 0);
            SetTrustersPercentage(thrDown, 0);
        }

        /// <summary>
        /// Перемещает дрон к конечной точке линейно по каждой из 3 осей
        /// </summary>
        /// <param name="target">Конечная точка</param>
        /// <param name="speedLimit">Ограничение скорости, в м/с</param>
        /// <param name="horizontalAligmentFirst">Сначала совмещение по горизонтальной плоскости (иначе - сначала совмещение по вертикальной оси)</param>
        /// <returns>true - если достиг конечной точки, false в остальных случаях</returns>
        private bool MovementOnVectorLinear(Vector3D target, float speedLimit, bool horizontalAligmentFirst)
        {
            
            remoteControl.DampenersOverride = true;
            Vector3D linearVelocity = remoteControl.GetShipVelocities().LinearVelocity;
            if (linearVelocity.Length() < speedLimit)
            {

                Vector3D pathVector = target - remoteControl.GetPosition();
                Vector3D pathVectorForward = remoteControl.WorldMatrix.Forward * pathVector.Dot(remoteControl.WorldMatrix.Forward);
                float ForwardScalar = (float)Vector3D.Normalize(pathVectorForward).Dot(remoteControl.WorldMatrix.Forward);

                Vector3D pathVectorRight = remoteControl.WorldMatrix.Right * pathVector.Dot(remoteControl.WorldMatrix.Right);
                float RightScalar = (float)Vector3D.Normalize(pathVectorRight).Dot(remoteControl.WorldMatrix.Right);

                Vector3D pathVectorUp = remoteControl.WorldMatrix.Up * pathVector.Dot(remoteControl.WorldMatrix.Up);
                float UpScalar = (float)Vector3D.Normalize(pathVectorUp).Dot(remoteControl.WorldMatrix.Up);

                if (linearVelocity.Length() < myScript.AcceptableMovingAccuracy && (pathVectorForward.Length() + pathVectorRight.Length() + pathVectorUp.Length()) / 3 < myScript.AcceptableMovingAccuracy)
                {
                    StopAllThrusters();
                    return true;
                }

                float shipMass = remoteControl.CalculateShipMass().PhysicalMass;

                Vector3D velocityForward = remoteControl.WorldMatrix.Forward * linearVelocity.Dot(remoteControl.WorldMatrix.Forward);
                Vector3D velocityRight = remoteControl.WorldMatrix.Right * linearVelocity.Dot(remoteControl.WorldMatrix.Right);
                Vector3D velocityUp = remoteControl.WorldMatrix.Up * linearVelocity.Dot(remoteControl.WorldMatrix.Up);

                float forwardVelScalar = (float)velocityForward.Dot(remoteControl.WorldMatrix.Forward);
                float stopDistForward = (float)(0.5 * shipMass * Math.Pow(forwardVelScalar, 2) / (forwardVelScalar > 0 ? backwardThrustEff : forwardThrustEff));
                float rightVelScalar = (float)velocityRight.Dot(remoteControl.WorldMatrix.Right);
                float stopDistRight = (float)(0.5 * shipMass * Math.Pow(rightVelScalar, 2) / (rightVelScalar > 0 ? leftThrustEff : rightThrustEff));
                float upVelScalar = (float)velocityUp.Dot(remoteControl.WorldMatrix.Up);
                float stopDistUp = (float)(0.5 * shipMass * Math.Pow(upVelScalar, 2) / (upVelScalar > 0 ? downThrustEff : upThrustEff));

                LCD.WriteText($"forwardSc = {ForwardScalar}\n", false);
                LCD.WriteText($"rightSc = {RightScalar}\n", true);
                LCD.WriteText($"upSc = {UpScalar}\n", true);
                LCD.WriteText($"stopDistForward = {stopDistForward}\n", true);
                LCD.WriteText($"stopDistRight = {stopDistRight}\n", true);
                LCD.WriteText($"stopDistUp = {stopDistUp}\n", true);


                if (pathVectorForward.Length() > stopDistForward && pathVectorForward.Length() > myScript.AcceptableMovingAccuracy
                    && (horizontalAligmentFirst || pathVectorUp.Length() < myScript.AcceptableMovingAccuracy))
                {
                    SetAxisThrustsByScalar(thrForward, thrBackward, ForwardScalar);
                }
                else
                {
                    SetTrustersPercentage(thrForward, 0);
                    SetTrustersPercentage(thrBackward, 0);
                }

                if (pathVectorRight.Length() > stopDistRight && pathVectorRight.Length() > myScript.AcceptableMovingAccuracy
                    && (horizontalAligmentFirst || pathVectorUp.Length() < myScript.AcceptableMovingAccuracy))
                {
                    SetAxisThrustsByScalar(thrRight, thrLeft, RightScalar);
                }
                else
                {
                    SetTrustersPercentage(thrRight, 0);
                    SetTrustersPercentage(thrLeft, 0);
                }

                if (pathVectorUp.Length() > stopDistUp && pathVectorUp.Length() > myScript.AcceptableMovingAccuracy
                    && (!horizontalAligmentFirst || (pathVectorForward.Length() < myScript.AcceptableMovingAccuracy
                    && pathVectorRight.Length() < myScript.AcceptableMovingAccuracy)))
                {
                    SetAxisThrustsByScalar(thrUp, thrDown, UpScalar);
                }
                else
                {
                    SetTrustersPercentage(thrUp, 0);
                    SetTrustersPercentage(thrDown, 0);
                }
            }
            else
            {
                StopAllThrusters();
            }
            return false;
        }
        /// <summary>
        /// Перемещает дрон к конечной точке через точку апекса
        /// </summary>
        /// <param name="target">Конечная точка</param>
        /// <param name="speedLimit">Ограничение скорости</param>
        /// <returns>true - если достиг конечной точки, false в остальных случаях</returns>
        private bool MovementOnVectorArchwise(Vector3D target, float speedLimit)
        {
            Vector3D currentPos = remoteControl.GetPosition();
            if (!arcInitialized || !arcTargetPos.Equals(target))
            {
                arcStartPos = currentPos;
                arcTargetPos = target;
                arcIsAscending = true;
                arcInitialized = true;

                Vector3D midPoint = (arcStartPos + arcTargetPos) / 2.0;
                Vector3D planetUp = Vector3D.Normalize(midPoint - PlanetCenter);

                double elevationAboveSeaLevel = 0;
                remoteControl.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out elevationAboveSeaLevel);

                double currentDistFromCenter = (arcStartPos - PlanetCenter).Length();
                double seaLevelRadius = currentDistFromCenter - elevationAboveSeaLevel;
                double calculatedArcHeight = elevationAboveSeaLevel + ((target - currentPos).Length() * myScript.ArcHeightMult) + Math.Abs(((target - currentPos).Dot(planetUp)) * 2);
                double arcHeight = calculatedArcHeight > myScript.ArcHeightMaximum ? myScript.ArcHeightMaximum : calculatedArcHeight;
                double apexRadius = seaLevelRadius + arcHeight;
                arcApexPos = PlanetCenter + planetUp * apexRadius;

                Vector3D startToApex = arcApexPos - arcStartPos;
                Vector3D startToTarget = arcTargetPos - arcStartPos;
                arcPlaneNormal = Vector3D.Normalize(Vector3D.Cross(startToApex, startToTarget));

                if (arcPlaneNormal.LengthSquared() < 0.001)
                {
                    arcPlaneNormal = Vector3D.Normalize(midPoint - PlanetCenter);
                }
            }

            double distToTarget = Vector3D.Distance(currentPos, arcTargetPos);
            Vector3D linearVelocity = remoteControl.GetShipVelocities().LinearVelocity;

            if (distToTarget < myScript.AcceptableMovingAccuracy * 4 && linearVelocity.Length() <= 5)
            {
                StopAllThrusters();
                SetGyrosOverride(false);
                StopAllGyros();
                arcInitialized = false;
                arcIsAscending = true;
                return true;
            }
            Vector3D gravNorm = Vector3D.Normalize(remoteControl.GetNaturalGravity());
            Vector3D rejTarget = Vector3D.Reject(target - currentPos, gravNorm);
            Vector3D rejApex = Vector3D.Reject(arcApexPos - currentPos, gravNorm);
            arcIsAscending = rejApex.Dot(Vector3D.Normalize(rejTarget)) > 0 && arcIsAscending;

            OrientShipForArc();

            Vector3D currentTarget = arcIsAscending ? arcApexPos : arcTargetPos;
            Vector3D toTarget = currentTarget - currentPos;
            double distToCurrentTarget = toTarget.Length();
            toTarget.Normalize();

            Vector3D shipUp = remoteControl.WorldMatrix.Up;
            double speedAlongPath = Vector3D.Dot(linearVelocity, shipUp);
            double shipMass = remoteControl.CalculateShipMass().PhysicalMass;
            double stopDist = 0;
            double availableThrust = 0;

            if (speedAlongPath > 0)
            {

                availableThrust = downThrustEff - (shipMass * (remoteControl.GetNaturalGravity().Dot(Vector3D.Normalize(linearVelocity))));

                stopDist = (0.5 * shipMass * speedAlongPath * speedAlongPath) / availableThrust;
            }
            else
            {
                availableThrust = upThrustEff - (shipMass * (remoteControl.GetNaturalGravity().Dot(Vector3D.Normalize(linearVelocity))));
                stopDist = ((0.5 * shipMass * speedAlongPath * speedAlongPath) / availableThrust) + (linearVelocity.Length() * 0.03);
                
            }
            bool shouldAccelerate = distToCurrentTarget > stopDist && Math.Abs(linearVelocity.Length()) < speedLimit;

            if (arcIsAscending)
            {
                if (shouldAccelerate && speedAlongPath < speedLimit)
                {
                    SetTrustersPercentage(thrUp, 1);
                    SetTrustersPercentage(thrDown, 0);
                }
                else
                {
                    if (speedAlongPath > 0)
                    {
                        if (downThrustEff > 0)
                        {
                            SetTrustersPercentage(thrDown, 1);
                            SetTrustersPercentage(thrUp, 0);
                        }
                        else
                        {
                            StopAllThrusters();
                        }
                    }
                    else
                    {
                        StopAllThrusters();
                    }
                }
            }
            else
            {
                if (shouldAccelerate && Math.Abs(speedAlongPath) < speedLimit)
                {
                    if (downThrustEff > 0)
                    {
                        SetTrustersPercentage(thrDown, 1);
                        SetTrustersPercentage(thrUp, 0);
                    }
                    else
                    {
                        StopAllThrusters();
                    }
                }
                else
                {
                    if (speedAlongPath < 0)
                    {
                        SetTrustersPercentage(thrUp, 1);
                        SetTrustersPercentage(thrDown, 0);
                    }
                    else
                    {
                        StopAllThrusters();
                    }
                }
            }

            return false;
        }
        /// <summary>
        /// Ориентирет дрон, совмещая вектор Up с направлением к цели при подъеме, и вектор Down при снижении
        /// </summary>
        /// <returns>Возвращает true, если выравнивание завершено</returns>
        private bool OrientShipForArc()
        {
            Vector3D targetPoint = arcIsAscending ? arcApexPos : arcTargetPos;
            Vector3D toTargetNorm = Vector3D.Normalize(targetPoint - remoteControl.GetPosition());
            Vector3D axisTarget = toTargetNorm.Cross(remoteControl.WorldMatrix.Up);
            if (!arcIsAscending)
            {
                axisTarget = -axisTarget;
            }
            Vector3D forwardTargetVector = Vector3D.Normalize(Vector3D.Reject(arcTargetPos - remoteControl.GetPosition(), arcApexPos - remoteControl.GetPosition()));
            Vector3D axisForward = forwardTargetVector.Cross(remoteControl.WorldMatrix.Forward);
            if (forwardTargetVector.Dot(remoteControl.WorldMatrix.Forward) < 0)
            {
                axisForward = Vector3D.Normalize(axisForward);
            }

            float pitch = (float)axisTarget.Dot(remoteControl.WorldMatrix.Right);
            float roll = (float)axisTarget.Dot(remoteControl.WorldMatrix.Backward);
            float yaw = (float)axisForward.Dot(remoteControl.WorldMatrix.Up);
            

            foreach (IMyGyro gyro in gyros)
            {
                gyro.GyroOverride = true;
                gyro.Pitch = pitch * myScript.GyroMult;
                gyro.Roll = roll * myScript.GyroMult;
                gyro.Yaw = yaw * myScript.GyroMult;
            }

            return pitch + roll + yaw < 0.001;
        }


        private void SetTrustersNewtons(List<IMyThrust> list, float value)
        {
            if (list.Count == 0) return;
            foreach (IMyThrust thrust in list)
            {
                thrust.ThrustOverride = value / list.Count;
            }
        }
        /// <summary>
        /// Устанавливает тягу группе двигателей определенной оси в зависимости от значения скаляра перемещения по этой оси
        /// </summary>
        /// <param name="thrPositive">Группа двигателей, толкающая дрон в положительном направлении по оси</param>
        /// <param name="thrNegative">Группа двигателей, толкающая дрон в отрицательном направлении по оси</param>
        /// <param name="scalar">Значение скаляра перемещения по оси</param>
        private void SetAxisThrustsByScalar(List<IMyThrust> thrPositive, List<IMyThrust> thrNegative, float scalar)
        {
            if (scalar > 0)
            {
                SetTrustersPercentage(thrPositive, 1);
            }
            else
            {
                SetTrustersPercentage(thrNegative, 1);
            }
        }

        #endregion


        #region Прочие вспомогательные методы
        /// <summary>
        /// Проверяет заполненность хранилищ, водородных баков и заряда батарей
        /// </summary>
        /// <returns>true - если хранилища, водородные баки и батареи заполнены в пределах установленных пороговых значений</returns>
        private bool CheckStorageAndTanksAndBatteries()
        {
            return CheckStoragesInPercent() < myScript.StorageCapacityThreshold
                && CheckTanksInPercent() > myScript.TanksCapacityThreshold
                && CheckBatteriesInPercent() > myScript.BatteriesCapacityThreshold;
        }
        /// <summary>
        /// Возвращает текущую среднюю заполненность хранилищ
        /// </summary>
        /// <returns>Заполненность, в %</returns>
        private float CheckStoragesInPercent()
        {
            float maxFill = 0;
            float fill = 0;
            foreach (IMyInventoryOwner storage in storages)
            {
                fill += Convert.ToInt32(storage.GetInventory(0).CurrentVolume.RawValue);
                maxFill += Convert.ToInt32(storage.GetInventory(0).MaxVolume.RawValue);
            }
            fill = 100 * fill / maxFill;
            return fill;
        }
        /// <summary>
        /// Возвращает текущую среднюю заполненность водородных баков
        /// </summary>
        /// <returns>Заполненность, в %</returns>
        private float CheckTanksInPercent()
        {
            float H2_O2Count = 0;
            double H2_O2Filled = 0;
            foreach (IMyGasTank gastank in tanks)
            {
                H2_O2Filled += gastank.FilledRatio * 100;
                H2_O2Count++;
            }
            H2_O2Filled = H2_O2Filled / H2_O2Count;
            return (float)H2_O2Filled;
        }
        /// <summary>
        /// Возвращает текущий средний заряд батарей
        /// </summary>
        /// <returns>Заряд, в %</returns>
        private float CheckBatteriesInPercent()
        {
            float maxCharge = 0;
            float charge = 0;
            foreach (IMyBatteryBlock battery in batteries)
            {
                charge += battery.CurrentStoredPower;
                maxCharge += battery.MaxStoredPower;
            }
            charge = 100 * charge / maxCharge;
            return charge;
        }
        /// <summary>
        /// Устанавливает режим работы дрона
        /// </summary>
        /// <param name="status">Новый режим работы</param>
        private void SetStatus(string status)
        {
            myScript.CurrentStatus = status;
        }
        /// <summary>
        /// Устанавливает всем бурам дрона значение "Включено"
        /// </summary>
        /// <param name="enabled">Значение "Включено"</param>
        private void SetDrillsEnabled(bool enabled)
        {
            foreach (IMyShipDrill drill in drills)
            {
                drill.Enabled = enabled;
            }
        }
        /// <summary>
        /// Устанавливает всем коннекторам дрона значение "Включено"
        /// </summary>
        /// <param name="enabled">Значение "Включено"</param>
        private void SetConnectorsEnabled(bool enabled)
        {
            foreach (IMyShipConnector con in connectors)
            {
                con.Enabled = enabled;
            }
        }
        /// <summary>
        /// Устанавливает всем водородным бакам дрона значение "Накопитель"
        /// </summary>
        /// <param name="enabled">Значение "Накопитель"</param>
        private void SetTanksStockpile(bool enabled)
        {
            foreach (IMyGasTank gastank in tanks)
            {
                gastank.Stockpile = enabled;
            }
        }
        /// <summary>
        /// Устанавливает всем батареям дрона значение "Режим зарядки"
        /// </summary>
        /// <param name="recharge">Значение "Режим зарядки": true - Зарядка, false - Авто</param>
        private void SetBatteriesRecharge(bool recharge)
        {
            foreach (IMyBatteryBlock battery in batteries)
            {
                battery.ChargeMode = recharge ? ChargeMode.Recharge : ChargeMode.Auto;
            }
        }
        /// <summary>
        /// Создает разметку шахт
        /// </summary>
        /// <param name="shaftMarks">Лист меток шахт</param>
        /// <param name="initCoords">Стартовая точка</param>
        /// <param name="shaftM">Количество шахт в ширину</param>
        /// <param name="shaftN">Количество шахт в длину</param>
        private void CreateShaftMarks(ref List<ShaftMark> shaftMarks, Vector3D initCoords, byte shaftM, byte shaftN)
        {
            double elevationSurface;
            remoteControl.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevationSurface);

            double depthMult = (elevationSurface / (PlanetCenter - initCoords).Length()) * 2;


            if (shaftMarks.Count > 0)
            {
                shaftMarks.Clear();
            }
            for (int i = 0; i < shaftM; i++)
            {
                for (int j = 0; j < shaftN; j++)
                {
                    ShaftMark shaftMark = new ShaftMark();
                    shaftMark.m = i;
                    shaftMark.n = j;
                    shaftMark.isFinished = false;
                    shaftMark.startCoords = initCoords + ((i - ((float)(shaftM - 1) / 2)) * (sizeInMeters.X + myScript.MiningMargin)) * remoteControl.WorldMatrix.Right
                                                + ((j - ((float)(shaftN - 1) / 2)) * (sizeInMeters.Z + myScript.MiningMargin)) * remoteControl.WorldMatrix.Forward;
                    shaftMark.endCoords = ((PlanetCenter - shaftMark.startCoords) * depthMult) + shaftMark.startCoords;
                    shaftMarks.Add(shaftMark);
                }
            }
        }
        /// <summary>
        /// Возвращает метку текущей незавершенной шахты
        /// </summary>
        private ShaftMark GetCurrentShaft()
        {
            foreach (var mark in shaftMarks)
            {
                if (!mark.isFinished)
                {
                    return mark;
                }
            }
            return new ShaftMark();
        }



        // Testing
        private void PrintVector(Vector3D vector, string name, bool append, string colorHEX = "#FF00FF")
        {
            textPanel.WriteText($"GPS:{name}:{vector.X}:{vector.Y}:{vector.Z}:{colorHEX}:\n", append);
        }
        // Testing

        #endregion

        /// <summary>
        /// Метка шахты
        /// </summary>
        private class ShaftMark
        {
            /// <summary>
            /// Координаты начала шахты (верх)
            /// </summary>
            public Vector3D startCoords;
            /// <summary>
            /// Координаты конца шахты (низ)
            /// </summary>
            public Vector3D endCoords;
            /// <summary>
            /// Шахта завершена
            /// </summary>
            public bool isFinished;
            /// <summary>
            /// Порядок в разметке в ширину
            /// </summary>
            public int m;
            /// <summary>
            /// Порядок в разметке в длину
            /// </summary>
            public int n;
        }
    }

    

    






    




}