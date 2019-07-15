﻿using HutongGames.PlayMaker;
using SeanprCore;

namespace RandomizerMod.FsmStateActions
{
    internal class RandomizerBoolTest : FsmStateAction
    {
        private readonly string _boolName;
        private readonly FsmEvent _failEvent;
        private readonly bool _playerdata;
        private readonly FsmEvent _successEvent;

        public RandomizerBoolTest(string boolName, string failEventName, string successEventName,
            bool playerdata = false)
        {
            _boolName = boolName;
            _playerdata = playerdata;

            if (failEventName != null)
            {
                _failEvent = FsmEvent.EventListContains(failEventName)
                    ? FsmEvent.GetFsmEvent(failEventName)
                    : new FsmEvent(failEventName);
            }

            if (successEventName == null)
            {
                return;
            }

            _successEvent = FsmEvent.EventListContains(successEventName)
                ? FsmEvent.GetFsmEvent(successEventName)
                : new FsmEvent(successEventName);
        }

        public RandomizerBoolTest(string boolName, FsmEvent failEvent, FsmEvent successEvent, bool playerdata = false)
        {
            _boolName = boolName;
            _playerdata = playerdata;
            _failEvent = failEvent;
            _successEvent = successEvent;
        }

        public override void OnEnter()
        {
            if (_playerdata && Ref.PD.GetBool(_boolName) ||
                !_playerdata && MultiWorldMod.MultiWorldMod.Instance.Settings.GetBool(false, _boolName))
            {
                if (_successEvent != null)
                {
                    Fsm.Event(_successEvent);
                }
            }
            else
            {
                if (_failEvent != null)
                {
                    Fsm.Event(_failEvent);
                }
            }

            Finish();
        }
    }
}