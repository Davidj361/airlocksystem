using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript {
    partial class Program : MyGridProgram {
        

        // Class definitions
        private class Airlock {
            public List<IMyTerminalBlock> airVents { get; }
            public List<IMyTerminalBlock> inDoors { get; }
            public List<IMyTerminalBlock> outDoors { get; }
            public List<IMyTerminalBlock> inSensors { get; }
            public List<IMyTerminalBlock> outSensors { get; }

            public Airlock() {
                airVents = new List<IMyTerminalBlock>();
                inDoors = new List<IMyTerminalBlock>();
                outDoors = new List<IMyTerminalBlock>();
                inSensors = new List<IMyTerminalBlock>();
                outSensors = new List<IMyTerminalBlock>();
            }

            public bool AddVent(IMyTerminalBlock input) {
                if (input == null || airVents.Contains(input))
                    return false;
                airVents.Add(input);
                return true;
            }

            public bool AddInDoor(IMyTerminalBlock input) {
                if (input == null || inDoors.Contains(input))
                    return false;
                inDoors.Add(input);
                return true;
            }

            public bool AddOutDoor(IMyTerminalBlock input) {
                if (input == null || outDoors.Contains(input))
                    return false;
                outDoors.Add(input);
                return true;
            }

            public bool AddInSensor(IMyTerminalBlock input) {
                if (input == null || inSensors.Contains(input))
                    return false;
                inSensors.Add(input);
                return true;
            }

            public bool AddOutSensor(IMyTerminalBlock input) {
                if (input == null || outSensors.Contains(input))
                    return false;
                outSensors.Add(input);
                return true;
            }
        }

        // Global vars
        Dictionary<String, Airlock> airlocks = new Dictionary<String, Airlock>();
        IMyTextPanel _textPanel;
        IEnumerator<bool> _stateMachine;

        public Program() {
            // Retrieve the blocks we're going to use.
            _textPanel = GridTerminalSystem.GetBlockWithName("LCD Panel Test") as IMyTextPanel;

            // Initialize our state machine
            _stateMachine = MainState();

            // Signal the programmable block to run again in the next tick. Be careful on how much you
            // do within a single tick, you can easily bog down your game. The more ticks you do your
            // operation over, the better.

            // What is actually happening here is that we are _adding_ the Once flag to the frequencies.
            // By doing this we can have multiple frequencies going at any time.
            Runtime.UpdateFrequency |= UpdateFrequency.Once;
        }

        public void Main(string argument, UpdateType updateType) {
            // Usually I verify that the argument is empty or a predefined value before running the state
            // machine. This way we can use arguments to control the script without disturbing the
            // state machine and its timing. For the purpose of this example however, I will omit this.

            // We only want to run the state machine(s) when the update type includes the
            // "Once" flag, to avoid running it more often than it should. It shouldn't run
            // on any other trigger. This way we can combine state machine running with
            // other kinds of execution, like tool bar commands, sensors or what have you.

            if ((updateType & UpdateType.Once) == UpdateType.Once) {
                RunStateMachine();
            }
        }

        // ***MARKER: State Machine Execution
        public void RunStateMachine() {
            // If there is an active state machine, run its next instruction set.
            if (_stateMachine != null) {
                // If there are no more instructions, we stop and release the state machine.
                if (!_stateMachine.MoveNext()) {
                    _stateMachine.Dispose();
                    _stateMachine = null;
                }
                else {
                    // The state machine still has more work to do, so signal another run again, 
                    // just like at the beginning.
                    Runtime.UpdateFrequency |= UpdateFrequency.Once;
                }
            }
        }

        private IEnumerable<bool> InitializeVars() {
            // Does it have the [Airlock 1234 etc] syntax? What is its ID? Does it have an indicator for In or Out?
            // 1st group is the whole thing, 2nd group is its ID, 3rd group is the In/Out
            string pattern = @"^[^\n\[]*(\[Airlock (\d+)(?: ([^\]]+))?\])";
            // Create airlock groups by its ID and add the sub-group blocks to it
            List<IMyTerminalBlock> allBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(allBlocks);
            foreach (IMyTerminalBlock iter in allBlocks) {
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(iter.CustomName, pattern);
                if (!match.Success || match.Groups[2] == null)
                    continue;
                string id = match.Groups[2].Value;
                try {
                    Airlock tmp = new Airlock();
                    airlocks.Add(id, tmp);
                }
                catch (ArgumentException e) {
                    // Key already exists, add this block to the appropriate sub-group
                }


                if (iter is IMyAirVent)
                    airlocks[id].AddVent(iter);
                else if (iter is IMyDoor) {
                    // Is it In or Out?
                    if (match.Groups[3] == null)
                        Echo("Door not specified as 'In' or 'Out': Null");
                    else if (match.Groups[3].Value == "In")
                        airlocks[id].AddInDoor(iter);
                    else if (match.Groups[3].Value == "Out")
                        airlocks[id].AddOutDoor(iter);
                    else
                        Echo("Door not specified as 'In' or 'Out':" + iter.CustomName);
                }
                else if (iter is IMySensorBlock) {
                    // Is it In or Out?
                    if (match.Groups[3] == null)
                        Echo("Sensor not specified as 'In' or 'Out': null");
                    else if (match.Groups[3].Value == "In")
                        airlocks[id].AddInSensor(iter);
                    else if (match.Groups[3].Value == "Out")
                        airlocks[id].AddOutSensor(iter);
                    else
                        Echo("Sensor not specified as 'In' or 'Out': " + iter.CustomName);
                }
                else
                    Echo("Block in Airlock group isn't sensor, door, or vent. Type: " + iter.GetType().ToString());
            }
            yield return true;
        }

        // ***MARKER: State Machine Program
        public IEnumerator<bool> MainState() {
            while (true) {
                // Re-initialize global vars
                airlocks.Clear();
                foreach (var step in InitializeVars())
                    yield return step;

                // Look through all the sensors for each airblock group and process airlock cycle if needed
                foreach (var item in airlocks) {
                    Airlock iter = item.Value;
                    // Check if there are doors, sensor, and vents for the airlock
                    if (!iter.inDoors.Any() || !iter.outDoors.Any() || !iter.inSensors.Any() || !iter.outSensors.Any() || !iter.airVents.Any()) {
                        continue;
                    }
                    foreach (var step in ProcessAirlock(iter))
                        yield return step;
                }
                yield return true;
            }
        }

        private IEnumerable<bool> ProcessAirlock(Airlock iter) {
            IMyAirVent firstVent = iter.airVents.First() as IMyAirVent;
            if (firstVent.Status == VentStatus.Depressurizing || firstVent.Status == VentStatus.Pressurizing)
                yield return true;
            // Compare which side has the most people standing in
            int inCount = 0;
            int outCount = 0;
            foreach (IMySensorBlock s in iter.inSensors) {
                List<MyDetectedEntityInfo> entities = new List<MyDetectedEntityInfo>();
                s.DetectedEntities(entities);
                inCount = entities.Count;
            }
            foreach (IMySensorBlock s in iter.outSensors) {
                List<MyDetectedEntityInfo> entities = new List<MyDetectedEntityInfo>();
                s.DetectedEntities(entities);
                outCount = entities.Count;
            }

            if (outCount == 0 && inCount == 0)
                yield return true;
            else if (outCount > inCount) {
                foreach (var step in ExternalOpenAirlock(iter.airVents, iter.inDoors, iter.outDoors))
                    yield return step;
            }
            else {
                foreach (var step in InternalOpenAirlock(iter.airVents, iter.inDoors, iter.outDoors))
                    yield return step;
            }

            yield return true;
        }

        private IEnumerable<bool> InternalOpenAirlock(List<IMyTerminalBlock> vents, List<IMyTerminalBlock> inDoors, List<IMyTerminalBlock> outDoors) {
            IMyAirVent firstVent = vents.First() as IMyAirVent;
            if (!firstVent.Enabled) {
                foreach (var step in CloseDoors(outDoors))
                    yield return step;
                foreach (var step in MyWait(0.5))
                    yield return step;
                foreach (IMyAirVent vent in vents)
                    vent.Enabled = true;
                foreach (var step in MyWait(0.5))
                    yield return step;
                while (firstVent.Status != VentStatus.Pressurized) {
                    yield return true;
                }
                foreach (var step in MyWait(0.5))
                    yield return step;
                foreach (var step in OpenDoors(inDoors))
                    yield return step;
            }
        }

        private IEnumerable<bool> ExternalOpenAirlock(List<IMyTerminalBlock> vents, List<IMyTerminalBlock> inDoors, List<IMyTerminalBlock> outDoors) {
            IMyAirVent firstVent = vents.First() as IMyAirVent;
            if (firstVent.Enabled) {
                foreach (var step in CloseDoors(inDoors))
                    yield return step;
                foreach (var step in MyWait(0.5))
                    yield return step;
                foreach (IMyAirVent vent in vents)
                    vent.Enabled = false;
                foreach (var step in MyWait(0.5))
                    yield return step;
                foreach (var step in OpenDoors(outDoors))
                    yield return step;
            }
        }

        private IEnumerable<bool> CloseDoors(List<IMyTerminalBlock> doors) {
            IMyDoor testDoor = null;
            foreach (IMyDoor iter in doors) {
                iter.Enabled = true;
                iter.CloseDoor();
                testDoor = iter;
            }
            if (testDoor == null) {
                throw new Exception("Test Door Null");
            }
            while (testDoor.OpenRatio != 0f) {
                yield return true;
            }
            foreach (IMyDoor iter in doors) {
                iter.Enabled = false;
            }
        }

        private IEnumerable<bool> OpenDoors(List<IMyTerminalBlock> doors) {
            IMyDoor testDoor = null;
            foreach (IMyDoor iter in doors) {
                iter.Enabled = true;
                iter.OpenDoor();
                testDoor = iter;
            }
            if (testDoor == null) {
                throw new Exception("Test Door Null");
            }
            while (testDoor.OpenRatio != 1f) {
                yield return true;
            }
            foreach (IMyDoor iter in doors) {
                iter.Enabled = false;
            }
        }

        private IEnumerable<bool> MyWait(double time) {
            if (time <= 0)
                yield break;
            double waitTimer = time;
            yield return true;
            while (waitTimer >= 0) {
                waitTimer = (waitTimer - Runtime.TimeSinceLastRun.TotalSeconds);
                yield return true;
            }
        }
    }
}