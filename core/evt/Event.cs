﻿using System;

namespace xwcs.core.evt
{
    public class Event : EventArgs, IEvent
    {
        private object _sender;
        private object _type;
        protected object _data;

        public Event(object s, object t, object d)
        {
            sender = s;
            type = t;
            data = d;
        }

        public object sender
        {
            get { return _sender; }
            set { _sender = value; }
        }

        public object type
        {
            get { return _type; }
            set { _type = value; }
        }

        public object data
        {
            set { _data = value; }
            get { return _data; }
        }
    }
}