﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.ComponentModel;
using xwcs.core.db.model;
using System.Diagnostics;
using DevExpress.XtraDataLayout;

namespace xwcs.core.db.binding
{
	using attributes;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Container;
	using DevExpress.XtraEditors.Repository;
	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Views.Base;
	using DevExpress.XtraGrid.Views.Grid;
	using evt;
	using System.Collections;
	using System.Data;
	using System.Reflection;




	public class GridBindingSource : BindingSource, IDisposable, IDataBindingSource
	{
		private static manager.ILogger _logger =  manager.SLogManager.getInstance().getClassLogger(typeof(GridBindingSource));

		private IEditorsHost _editorsHost = null;
		private GridControl _grid = null; 
		private Type  _dataType = null;
		

		private Dictionary<string, IList<CustomAttribute>> _attributesCache = new Dictionary<string, IList<CustomAttribute>>();
		private Dictionary<string, RepositoryItem> _repositories = new Dictionary<string, RepositoryItem>();
		private bool _gridIsConnected = false;

        // hold last position before position changed occurs
        private int _oldPosition = -1;


		public Dictionary<string, IList<CustomAttribute>> AttributesCache { get { return _attributesCache; } }

		//if true it will handle eventual column text override
		public bool HandleCustomColumnDisplayText { get; set; } = false;


		public GridBindingSource() : this((IEditorsHost)null) { }
		public GridBindingSource(IContainer c) : this(null, c) { }
		public GridBindingSource(object o, string s) : this(null, o, s) { }
		public GridBindingSource(IEditorsHost eh) : base() { start(eh); }
		public GridBindingSource(IEditorsHost eh, IContainer c) : base(c){ start(eh); }
		public GridBindingSource(IEditorsHost eh, object o, string s) : base(o, s){ start(eh); }

		private void start(IEditorsHost eh)
		{
			_editorsHost = eh;
			if (_editorsHost != null && _editorsHost.FormSupport != null)
			{
				_editorsHost.FormSupport.AddBindingSource(this);
			}
        }
        
        #region IDisposable Support
        protected bool disposedValue = false; // To detect redundant calls
		protected override void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					//disconnect events in any case
					if (_grid != null)
					{
						_grid.DataSourceChanged -= GridDataSourceChanged;
						GridView gv = _grid.MainView as GridView;
						if (gv != null)
						{
							gv.CustomRowCellEditForEditing -= CustomRowCellEditForEditingHandler;
							gv.ShownEditor -= EditorShownHandler;
							gv.CustomColumnDisplayText -= CustomColumnDisplayText; //try remove anyway
						}
						//remove eventual grid cells editor repositories from grid
						foreach (RepositoryItem ri in _repositories.Values)
						{
							_grid.RepositoryItems.Remove(ri);
						}
						_grid = null;
					}
					if (DataSource != null)
					{
						DataSource = null;
					}
					//clear cached attributes and repositories
					resetAttributes();

					ListChanged -= handleListItemChanged;
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposedValue = true;
				//call inherited
				base.Dispose(disposing);
			}
		}
		#endregion


        public Type DataType
        {
            get
            {
                return _dataType;
            }
        }
		
		public IEditorsHost EditorsHost
		{
			get
			{
				return _editorsHost;
			}
		}

		public new object DataSource {
			get {
				return base.DataSource; 
			}

			set {
                if (value == null)
                {
                    // just empty grid rows
                    if(!ReferenceEquals(null, base.DataSource))
                    {
                        base.DataSource = null;
                    }

                    // if grid was not connected , manage remove useless columns
                    if (!_gridIsConnected)
                    {
                        if (!ReferenceEquals(null, _grid) && _grid.MainView is ColumnView)
                        {
                            (_grid.MainView as ColumnView).Columns.Clear();
                        }
                    }

                    return;
                }

				Type t = null;

				object tmpDs = null;

				//read annotations
				//here it depends what we have as DataSource, it can be Object, Type or IList Other we will ignore
				BindingSource bs = value as BindingSource;
				if (bs == null)
				{
					//no binding source
					tmpDs = value;
				}
				else
				{
					tmpDs = bs.DataSource;
				}

				Type tmpT = tmpDs as Type;
				if (tmpT == null)
				{
					//lets try another way, maybe IList
					if (tmpDs is IList)
					{
						//try to obtain element type
						t = (tmpDs as IList).GetType().GetGenericArguments()[0];
					}
					else if (tmpDs is IEnumerable)
					{
						//try to obtain element type
						t = (tmpDs as IEnumerable).GetType().GetGenericArguments()[0];
					}
					else if (tmpDs is IListSource)
					{
						//try to obtain element type
						t = (tmpDs as IListSource).GetType().GetGenericArguments()[0];
					}
					else
					{
						//it should be plain object and try to take type
						if ((tmpDs as object) != null)
						{
							t = tmpDs.GetType();
						}
						else
						{
							_logger.Error("Missing DataSource for grid layout");
							return; // no valid binding arrived so we skip 
						}
					}
				}
				else {
					t = tmpT;
				}

                // now when we know type we reset old handler
                if (!ReferenceEquals(null, _dataType))
                {
                    ListChanged -= handleListItemChanged;
                }

                // make generic Structure watch basing on type of DataSource element
                _oldPosition = -1;

				// _dataType musty be set before DS cause event can come in not correct order!!!!
				// we use timer on form with force message queue execution
				_dataType = t;
				base.DataSource = value;
				

                // now when we know type we set new handler
                if(!ReferenceEquals(null, _dataType))
                {
                    ListChanged += handleListItemChanged;
                }            
			}
		}

		public GridControl Grid {
			get {
				return _grid;
			}

			set {
#if DEBUG_TRACE_LOG_ON
				_logger.Debug("Set-GRID : New");
#endif
				if (_grid == value) return;
				//first disconnect eventual old one
				if (_grid != null)
				{
					_grid.DataSourceChanged -= GridDataSourceChanged;
					GridView gv = _grid.MainView as GridView;
					if (gv != null)
					{
						gv.CustomRowCellEditForEditing -= CustomRowCellEditForEditingHandler;
						gv.ShownEditor -= EditorShownHandler;
						gv.CustomColumnDisplayText -= CustomColumnDisplayText;
						gv.DataController.ListSourceChanged += DataController_ListSourceChanged;
					}
				}
				_grid = value;
				if(_grid.MainView is ColumnView){
					(_grid.MainView as ColumnView).OptionsBehavior.AutoPopulateColumns = true;
					(_grid.MainView as ColumnView).DataController.ListSourceChanged += DataController_ListSourceChanged;
				}
				_grid.DataSourceChanged += GridDataSourceChanged;
				//connect
				_grid.DataSource = this;
			}
		}

		
		private void DataController_ListSourceChanged(object sender, EventArgs e)
		{
			ConnectGrid();
		}
		private void GridDataSourceChanged(object sender, EventArgs e)
		{
			ConnectGrid();
		}

		public void addNewRecord(object rec)
		{
			AddNew();
			Current.CopyFrom(rec);
		}

        public void setCurrentRecord(object rec)
		{
			Current.CopyFrom(rec);
		}

		

        
		private void ConnectGrid() {
			if (_grid != null && !_gridIsConnected && _dataType != null && _grid.MainView.DataController.IsReady)
			{
				// check columns loaded
				if ((_grid.MainView is ColumnView) && ((_grid.MainView as ColumnView).Columns.Count == 0))
				{
					_grid.MainView.PopulateColumns();
				}


				PropertyInfo[] pis = _dataType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
				foreach (PropertyInfo pi in pis) {
					IEnumerable<CustomAttribute> attrs = ReflectionHelper.GetCustomAttributesFromPath(_dataType, pi.Name);
					IList<CustomAttribute> ac = new List<CustomAttribute>();
					foreach (CustomAttribute a in attrs)
					{
                        DevExpress.XtraGrid.Columns.GridColumn gc = null;
                        if (_grid.MainView is ColumnView)
                        {
                            gc = (_grid.MainView as ColumnView).Columns.ColumnByFieldName(pi.Name);
                        }
						GridColumnPopulated gcp = new GridColumnPopulated { FieldName = pi.Name, RepositoryItem = null, Column = gc };
						a.applyGridColumnPopulation(this, gcp);
						RepositoryItem ri = gcp.RepositoryItem;
						ac.Add(a as CustomAttribute);
						if(ri != null) {
							_grid.RepositoryItems.Add(ri);
							_repositories[pi.Name] = ri;
							if (gc != null) gc.ColumnEdit = ri;
						}						
					}
					if (ac.Count > 0)
						_attributesCache[pi.Name] = ac;

				}
				//attach CustomRowCellEditForEditing event too
				GridView gv = _grid.MainView as GridView;
				if(gv!=null) {
					gv.CustomRowCellEditForEditing += CustomRowCellEditForEditingHandler;
					gv.ShownEditor += EditorShownHandler;
					if (HandleCustomColumnDisplayText)
						gv.CustomColumnDisplayText += CustomColumnDisplayText;
				}

				_gridIsConnected = true;
			}
		}

        
		protected virtual void GetFieldDisplayText(object sender, CustomColumnDisplayTextEventArgs e) {
			//just to be overloaded if necessary	
		}
        

		private void CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e){

            if (_attributesCache.ContainsKey(e.Column.FieldName))
            {
                foreach (CustomAttribute a in _attributesCache[e.Column.FieldName])
                {
                    a.applyGetFieldDisplayText(this, e);
                }
            }

            // eventual override
            GetFieldDisplayText(sender, e);
        }

		private void EditorShownHandler(object sender, EventArgs e) {
			ColumnView view = (ColumnView)sender;
			Control editor = view.ActiveEditor;
			RepositoryItem be = ((BaseEdit)editor).Properties;
			ViewEditorShownEventArgs vsea = new ViewEditorShownEventArgs
			{
				Control = editor,
				View = view,
				FieldName = view.FocusedColumn.FieldName,
				RepositoryItem = be
			};

			if (_attributesCache.ContainsKey(vsea.FieldName))
			{
				foreach (CustomAttribute a in _attributesCache[vsea.FieldName])
				{
					a.applyCustomEditShown(this, vsea);
				}
			}
		}

		private void CustomRowCellEditForEditingHandler(object sender, CustomRowCellEditEventArgs e) {
			if (_repositories.ContainsKey(e.Column.FieldName))
			{
				e.RepositoryItem = _repositories[e.Column.FieldName];
			}
			if (_attributesCache.ContainsKey(e.Column.FieldName))
			{
				foreach (CustomAttribute a in _attributesCache[e.Column.FieldName])
				{
					a.applyCustomRowCellEdit(this, e);
				}
			}
		}

		//if there is change or we dispose we need reset attributes
		private void resetAttributes()
		{
			_attributesCache.Values.ToList().ForEach(e => { e.ToList().ForEach(a => a.unbind(this)); e.Clear(); });
			_attributesCache.Clear();
			//clear repositories cache	
			_repositories.Values.ToList().ForEach(r => r.Dispose());				
			_repositories.Clear();
		}


		protected virtual void resetSlavesOfModifiedProperty(ResetSlavesAttribute att) {
			//should be overridden

		}

		private void handleListItemChanged(object sender, ListChangedEventArgs args)
		{
			if(args.ListChangedType == ListChangedType.ItemChanged)
			{
				if(args.PropertyDescriptor != null) {
					IEnumerable<CustomAttribute> attrs = ReflectionHelper.GetCustomAttributesFromPath(_dataType, args.PropertyDescriptor.Name);
					var ra = attrs.OfType<ResetSlavesAttribute>();
					if(ra.Count() == 1) {
						//we need reset slaves chain
						resetSlavesOfModifiedProperty(ra.First());
					}
					
#if DEBUG_TRACE_LOG_ON
					_logger.Debug("Item changed! " + args.PropertyDescriptor.Name);
#endif
				}
			}
		}

        public Control GetControlByModelProperty(string ModelropertyName)
        {
            return null;
        }

        public void SuspendLayout()
        {
            return;
        }

        public void ResumeLayout()
        {
            return;
        }


        protected override void OnPositionChanged(EventArgs e)
        {
            if (_oldPosition == Position) return; // skip call on the same line
            base.OnPositionChanged(e);
            //store current in old
            _oldPosition = Position; // this means first one will not exist!!!
        }

        public bool ChangeLayout(string LayoutSuffix)
        {
            throw new NotImplementedException();
        }

        public int LastPosition
        {
            get
            {
                return _oldPosition;
            }
        }

        
    }
}
