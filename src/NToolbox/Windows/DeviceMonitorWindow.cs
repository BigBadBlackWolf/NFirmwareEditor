﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using JetBrains.Annotations;
using NCore;
using NCore.Serialization;
using NCore.UI;
using NCore.USB;
using NToolbox.Models;

namespace NToolbox.Windows
{
	public partial class DeviceMonitorWindow : EditorDialogWindow
	{
		private const string ConfigurationFileName = "NDeviceMonitor.xml";

		private const int MaxItems = 1200;
		private const int MarkerSize = 0;
		private const int SelectedMarkerSize = 7;

		private readonly HidConnector m_connector = new HidConnector();

		private DeviceMonitorConfiguration m_configuration;
		private IDictionary<string, SeriesRelatedData> m_seriesData;
		private TimeSpan m_timeFrame = TimeSpan.FromSeconds(10);
		private DateTime? m_startTime;
		private bool m_isTracking = true;

		private ContextMenu m_timeFrameMenu;
		private ContextMenu m_puffsMenu;
		private bool m_isPaused;

		private bool m_isRecording;
		private DateTime m_recordStartTime = DateTime.Now;
		private StreamWriter m_fileWriter;
		private readonly StringBuilder m_lineBuilder = new StringBuilder();

		public bool IsTracking
		{
			get { return m_isTracking; }
			set
			{
				m_isTracking = value;
				TrackingButton.Enabled = !m_isTracking;
			}
		}

		public DeviceMonitorWindow()
		{
			InitializeComponent();
			Initialize();
			InitializeControls();
			InitializeChart();
			InitializeSeries();
			InitializeContextMenus();
		}

		private void Initialize()
		{
			try
			{
				using (var fs = File.OpenRead(Path.Combine(Paths.ApplicationDirectory, ConfigurationFileName)))
				{
					m_configuration = Serializer.Read<DeviceMonitorConfiguration>(fs);
				}
			}
			catch (Exception)
			{
				// Ignore
			}
			finally
			{
				if (m_configuration == null || m_configuration.ActiveSeries == null) m_configuration = new DeviceMonitorConfiguration();
			}

			Opacity = 0;
			Load += (s, e) =>
			{
				if (!EnsureConnection()) return;

				Opacity = 1;
				new Thread(MonitoringProc) { IsBackground = true }.Start();
			};
			Closing += (s, e) =>
			{
				try
				{
					SaveCheckedSeries();
					using (var fs = File.Create(Path.Combine(Paths.ApplicationDirectory, ConfigurationFileName)))
					{
						Serializer.Write(m_configuration, fs);
					}
				}
				catch (Exception)
				{
					// Ignore
				}
			};
		}

		private void MonitoringProc()
		{
			while (true)
			{
				if (!m_isPaused)
				{
					byte[] bytes;
					try
					{
						bytes = m_connector.ReadMonitoringData();
					}
					catch (Exception)
					{
						break;
					}

					var data = BinaryStructure.Read<MonitoringData>(bytes);
					var kvp = CreateMonitoringDataCollection(data);

					UpdateUI(() => UpdateSeries(kvp));
				}
				Thread.Sleep(100);
			}

			if (EnsureConnection()) MonitoringProc();
		}

		private void InitializeControls()
		{
			var batteryLimits = new[] { new ValueLimit<float, int>(2.75f, 80), new ValueLimit<float, int>(4.2f, 95) };
			var batteryPackLimits = new[] { new ValueLimit<float, int>(2.75f, 80), new ValueLimit<float, int>(12.6f, 95) };
			var powerLimits = new[] { new ValueLimit<float, int>(1, 50), new ValueLimit<float, int>(75, 80) };
			var powerSetLimits = new[] { new ValueLimit<float, int>(1, 50), new ValueLimit<float, int>(75, 80) };
			var tempLimits = new[] { new ValueLimit<float, int>(100, 50), new ValueLimit<float, int>(600, 80) };
			var tempSetLimits = new[] { new ValueLimit<float, int>(100, 50), new ValueLimit<float, int>(600, 80) };
			var resistanceLimits = new[] { new ValueLimit<float, int>(0.05f, 30), new ValueLimit<float, int>(3f, 50) };
			var realResistanceLimits = new[] { new ValueLimit<float, int>(0.05f, 30), new ValueLimit<float, int>(3f, 50) };
			var outputVoltageLimits = new[] { new ValueLimit<float, int>(1, 10), new ValueLimit<float, int>(9, 30) };
			var outputCurrentLimits = new[] { new ValueLimit<float, int>(1, 10), new ValueLimit<float, int>(50, 30) };
			var boardTemperatureLimits = new[] { new ValueLimit<float, int>(0, 1), new ValueLimit<float, int>(99, 10) };

			m_seriesData = new Dictionary<string, SeriesRelatedData>
			{
				{
					SensorsKeys.Battery1,
					new SeriesRelatedData(Color.DarkSlateGray, Battery1CheckBox, Battery1Panel, Battery1VoltageLabel, "{0} V", batteryLimits)
				},
				{
					SensorsKeys.Battery2,
					new SeriesRelatedData(Color.DarkSlateGray, Battery2CheckBox, Battery2Panel, Battery2VoltageLabel, "{0} V", batteryLimits)
				},
				{
					SensorsKeys.Battery3,
					new SeriesRelatedData(Color.DarkSlateGray, Battery3CheckBox, Battery3Panel, Battery3VoltageLabel, "{0} V", batteryLimits)
				},
				{
					SensorsKeys.BatteryPack,
					new SeriesRelatedData(Color.DarkSlateGray, BatteryPackCheckBox, BatteryPackPanel, BatteryPackVoltageLabel, "{0} V", batteryPackLimits)
				},
				{
					SensorsKeys.Power,
					new SeriesRelatedData(Color.LimeGreen, PowerCheckBox, PowerPanel, PowerLabel, "{0} W", powerLimits)
				},
				{
					SensorsKeys.PowerSet,
					new SeriesRelatedData(Color.Green, PowerSetCheckBox, PowerSetPanel, PowerSetLabel, "{0} W", powerSetLimits)
				},
				{
					SensorsKeys.Temperature,
					new SeriesRelatedData(Color.Red, TemperatureCheckBox, TemperaturePanel, TemperatureLabel, "{0} °C", tempLimits)
				},
				{
					SensorsKeys.TemperatureSet,
					new SeriesRelatedData(Color.DarkRed, TemperatureSetCheckBox, TemperatureSetPanel, TemperatureSetLabel, "{0} °C", tempSetLimits)
				},
				{
					SensorsKeys.OutputCurrent,
					new SeriesRelatedData(Color.Orange, OutputCurrentCheckBox, OutputCurrentPanel, OutputCurrentLabel, "{0} A", outputCurrentLimits)
				},
				{
					SensorsKeys.OutputVoltage,
					new SeriesRelatedData(Color.LightSkyBlue, OutputVoltageCheckBox, OutputVoltagePanel, OutputVoltageLabel, "{0} V", outputVoltageLimits)
				},
				{
					SensorsKeys.Resistance,
					new SeriesRelatedData(Color.Violet, ResistanceCheckBox, ResistancePanel, ResistanceLabel, "{0} Ω", resistanceLimits)
				},
				{
					SensorsKeys.RealResistance,
					new SeriesRelatedData(Color.BlueViolet, RealResistanceCheckBox, RealResistancePanel, RealResistanceLabel, "{0} Ω", realResistanceLimits)
				},
				{
					SensorsKeys.BoardTemperature,
					new SeriesRelatedData(Color.SaddleBrown, BoardTemperatureCheckBox, BoardTemperaturePanel, BoardTemperatureLabel, "{0} °C", boardTemperatureLimits)
				}
			};

			PauseButton.Click += (s, e) =>
			{
				m_isPaused = !m_isPaused;
				PauseButton.Text = m_isPaused ? "Resume" : "Pause";
			};

			TrackingButton.Click += (s, e) => ChangeTimeFrameAndTrack(m_timeFrame);
			RecordButton.Click += (s, e) =>
			{
				if (m_isRecording)
				{
					StopRecording();
				}
				else
				{
					StartRecording();
				}
			};
		}

		private void InitializeChart()
		{
			MainChart.Palette = ChartColorPalette.Pastel;
			var area = new ChartArea();
			{
				area.AxisX.IsMarginVisible = false;
				area.AxisX.MajorGrid.Enabled = true;
				area.AxisX.MajorGrid.LineColor = Color.FromArgb(230, 230, 230);
				area.AxisX.MajorTickMark.TickMarkStyle = TickMarkStyle.None;
				area.AxisX.LabelStyle.Enabled = false;
				area.AxisX.LineColor = Color.DarkGray;
				area.AxisX.IntervalOffsetType = DateTimeIntervalType.Milliseconds;
				area.AxisX.ScaleView.Zoomable = true;
				area.AxisX.ScrollBar.Enabled = false;

				area.AxisY.IsMarginVisible = false;
				area.AxisY.MajorGrid.Enabled = true;
				area.AxisY.MajorGrid.LineColor = Color.FromArgb(230, 230, 230);
				area.AxisY.MajorTickMark.TickMarkStyle = TickMarkStyle.None;
				area.AxisY.LabelStyle.Enabled = false;
				area.AxisY.LineColor = Color.DarkGray;
			}
			var valueAnnotation = new CalloutAnnotation
			{
				AxisX = area.AxisX,
				AxisY = area.AxisY
			};
			MainChart.ChartAreas.Add(area);
			MainChart.Annotations.Add(valueAnnotation);

			DataPoint pointUnderCursor = null;
			var isPlacingTooltip = false;
			MainChart.MouseMove += (s, e) =>
			{
				if (isPlacingTooltip) return;

				try
				{
					isPlacingTooltip = true;
					var result = MainChart.HitTest(e.X, e.Y);

					if (result.ChartElementType != ChartElementType.DataPoint ||
					    result.PointIndex < 0 ||
					    result.Series.Points.Count <= result.PointIndex)
					{
						return;
					}

					if (result.Series.Points.Count <= result.PointIndex) return;
					if (pointUnderCursor != null) pointUnderCursor.MarkerSize = MarkerSize;

					pointUnderCursor = result.Series.Points[result.PointIndex];
					pointUnderCursor.MarkerSize = SelectedMarkerSize;

					valueAnnotation.BeginPlacement();

					// You must set AxisX before binding to xValue!
					valueAnnotation.AnchorX = pointUnderCursor.XValue;
					valueAnnotation.AnchorY = pointUnderCursor.YValues[0];
					valueAnnotation.Text = pointUnderCursor.Tag.ToString();

					valueAnnotation.EndPlacement();
				}
				finally
				{
					isPlacingTooltip = false;
				}
			};

			MainChartScrollBar.Scroll += (s, e) => IsTracking = MainChartScrollBar.Value == MainChartScrollBar.Maximum;
			MainChartScrollBar.ValueChanged += (s, e) => ScrollChart(false);
		}

		private void InitializeSeries()
		{
			foreach (var kvp in m_seriesData)
			{
				var seriesName = kvp.Key;
				var data = kvp.Value;

				data.Seires = CreateSeries(seriesName, data.Color);
				MainChart.Series.Add(data.Seires);

				bool isChecked;
				if (!m_configuration.ActiveSeries.TryGetValue(seriesName, out isChecked)) isChecked = true;

				data.CheckBox.Tag = seriesName;
				data.CheckBox.Checked = data.Seires.Enabled = isChecked;
				data.CheckBox.CheckedChanged += SeriesCheckBox_CheckedChanged;
				data.Panel.BackColor = data.Color;
			}
		}

		private void InitializeContextMenus()
		{
			m_timeFrameMenu = new ContextMenu(new[]
			{
				new MenuItem("5 seconds",  (s, e) => ChangeTimeFrameAndTrack(TimeSpan.FromSeconds(5))),
				new MenuItem("10 seconds", (s, e) => ChangeTimeFrameAndTrack(TimeSpan.FromSeconds(10))),
				new MenuItem("20 seconds", (s, e) => ChangeTimeFrameAndTrack(TimeSpan.FromSeconds(20))),
				new MenuItem("30 seconds", (s, e) => ChangeTimeFrameAndTrack(TimeSpan.FromSeconds(30))),
				new MenuItem("45 seconds", (s, e) => ChangeTimeFrameAndTrack(TimeSpan.FromSeconds(45))),
				new MenuItem("60 seconds", (s, e) => ChangeTimeFrameAndTrack(TimeSpan.FromSeconds(60)))
			});
			TimeFrameButton.Click += (s, e) =>
			{
				var control = (Control)s;
				m_timeFrameMenu.Show(control, new Point(control.Width, 0));
			};

			m_puffsMenu = new ContextMenu();
			for (var i = 1; i <= 9; i++)
			{
				var seconds = i;
				m_puffsMenu.MenuItems.Add(seconds + (seconds == 1 ? " second" : " seconds"), (s, e) => PuffMenuItem_Click(seconds));
			}
			PuffButton.Click += (s, e) =>
			{
				var control = (Control)s;
				m_puffsMenu.Show(control, new Point(control.Width, 0));
			};
		}

		private bool EnsureConnection()
		{
			if (m_connector.IsDeviceConnected) return true;

			var result = InfoBox.Show
			(
				"No compatible USB devices are connected." +
				"\n\n" +
				"To continue, please connect one." +
				"\n\n" +
				"If one already IS connected, try unplugging and plugging it back in. The cable may be loose.",
				MessageBoxButtons.OKCancel
			);

			if (result == DialogResult.OK)
			{
				return EnsureConnection();
			}
			if (result == DialogResult.Cancel)
			{
				UpdateUI(Close);
				return false;
			}
			return true;
		}

		private Series CreateSeries(string name, Color color)
		{
			var series = new Series
			{
				Name = name,
				ChartType = SeriesChartType.Line,
				XValueType = ChartValueType.DateTime,
				YValueType = ChartValueType.Double,
				Color = color,
				BorderWidth = 2,
				SmartLabelStyle =
				{
					Enabled = true,
					AllowOutsidePlotArea = LabelOutsidePlotAreaStyle.Yes,
					IsOverlappedHidden = false,
					IsMarkerOverlappingAllowed = true,
					MinMovingDistance = 1,
					CalloutStyle = LabelCalloutStyle.None,
					CalloutLineDashStyle = ChartDashStyle.Solid,
					CalloutLineAnchorCapStyle = LineAnchorCapStyle.None,
					CalloutLineWidth = 0,
					MovingDirection = LabelAlignmentStyles.BottomLeft
				}
			};
			return series;
		}

		private void ChangeTimeFrameAndTrack(TimeSpan timeFrame)
		{
			m_timeFrame = timeFrame;
			MainChartScrollBar.Value = MainChartScrollBar.Maximum;
			ScrollChart(true);
			IsTracking = true;
		}

		private IDictionary<string, float> CreateMonitoringDataCollection(MonitoringData data)
		{
			var battery1 = data.Battery1Voltage == 0 ? 0 : (data.Battery1Voltage + 275) / 100f;
			var battery2 = data.Battery2Voltage == 0 ? 0 : (data.Battery2Voltage + 275) / 100f;
			var battery3 = data.Battery3Voltage == 0 ? 0 : (data.Battery3Voltage + 275) / 100f;
			var batteryPack = battery1 + battery2 + battery3;

			var outputVoltage = data.OutputVoltage / 100f;
			var outputCurrent = data.OutputCurrent / 100f;
			var outputPower = outputVoltage * outputCurrent;

			return new Dictionary<string, float>
			{
				{ SensorsKeys.Timestamp, data.Timestamp / 100f },

				{ SensorsKeys.IsFiring, data.IsFiring ? 1 : 0 },
				{ SensorsKeys.IsCharging, data.IsCharging ? 1 : 0 },
				{ SensorsKeys.IsCelcius, data.IsCelcius ? 1 : 0 },

				{ SensorsKeys.Battery1, battery1 },
				{ SensorsKeys.Battery2, battery2 },
				{ SensorsKeys.Battery3, battery3 },
				{ SensorsKeys.BatteryPack, batteryPack },

				{ SensorsKeys.Power, outputPower },
				{ SensorsKeys.PowerSet, data.PowerSet / 10f },
				{ SensorsKeys.TemperatureSet, data.TemperatureSet },
				{ SensorsKeys.Temperature, data.Temperature },

				{ SensorsKeys.OutputVoltage, outputVoltage },
				{ SensorsKeys.OutputCurrent, outputCurrent },

				{ SensorsKeys.Resistance, data.Resistance / 1000f },
				{ SensorsKeys.RealResistance, data.RealResistance / 1000f },

				{ SensorsKeys.BoardTemperature, data.BoardTemperature }
			};
		}

		private void UpdateSeries(IDictionary<string, float> sensors)
		{
			if (!m_startTime.HasValue) m_startTime = DateTime.Now;

			var isCelcius = sensors[SensorsKeys.IsCelcius] > 0;
			m_seriesData[SensorsKeys.Temperature].SetLastValueFormat(isCelcius ? "{0} °C" : "{0} °F");
			m_seriesData[SensorsKeys.TemperatureSet].SetLastValueFormat(isCelcius ? "{0} °C" : "{0} °F");

			var now = DateTime.Now;
			var xValue = now.ToOADate();
			var xAxisMax = now.AddSeconds(1).ToOADate();
			foreach (var kvp in m_seriesData)
			{
				var sensorName = kvp.Key;
				var data = kvp.Value;
				var readings = sensors[sensorName];
				var interpolatedValue = Interpolate(readings, data.InterpolationLimits);

				var point = new DataPoint();
				if (Math.Abs(readings) > 0.001)
				{
					var roundedValue = (float)Math.Round(readings, 3);
					point.XValue = xValue;
					point.YValues = new double[] { interpolatedValue };
					point.Tag = point.Label = roundedValue.ToString(CultureInfo.InvariantCulture);
					point.MarkerSize = MarkerSize;
					point.MarkerStyle = MarkerStyle.Circle;
					data.SetLastValue(roundedValue);
				}
				else
				{
					point.IsEmpty = true;
					data.SetLastValue(null);
				}
				data.Seires.Points.Add(point);
			}

			if (m_isRecording)
			{
				m_lineBuilder.Clear();
				m_lineBuilder.Append((now - m_recordStartTime).TotalSeconds.ToString(CultureInfo.InvariantCulture));
				m_lineBuilder.Append(",");

				var values = m_seriesData.Values
										 .Where(x => x.CheckBox.Checked)
										 .Select(x => x.LastValue.HasValue ? x.LastValue.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);

				m_lineBuilder.Append(string.Join(",", values));
				var ex = Safe.Execute(() =>
				{
					m_fileWriter.WriteLine(m_lineBuilder.ToString());
					m_fileWriter.Flush();
				});
				if (ex != null)
				{
					InfoBox.Show("Recording was stopped because of error:\n" + ex.Message);
					RecordButton.PerformClick();
				}
			}

			foreach (var series in MainChart.Series)
			{
				while (series.Points.Count > MaxItems)
				{
					series.Points.RemoveAt(0);
				}

				if (series.Points.Count > 0)
				{
					var lastPoint = series.Points[series.Points.Count - 1];
					if (lastPoint.IsEmpty) continue;

					if (series.Points.Count > 1)
					{
						var preLastPoint = series.Points[series.Points.Count - 2];
						preLastPoint.Label = null;
						//preLastPoint.MarkerSize = 0;
					}
				}
			}

			var points = MainChart.Series.SelectMany(x => x.Points).Where(x => !x.IsEmpty).ToArray();

			var minDate = DateTime.FromOADate(points.Min(x => x.XValue));
			var maxDate = DateTime.FromOADate(points.Max(x => x.XValue));

			var range = maxDate - minDate;
			var framesCount = Math.Floor(range.TotalSeconds / m_timeFrame.TotalSeconds);

			MainChartScrollBar.Maximum = (int)(framesCount * 30);
			if (IsTracking)
			{
				MainChartScrollBar.Value = MainChartScrollBar.Maximum;
				ScrollChart(true);
			}

			MainChart.ChartAreas[0].AxisX.Minimum = m_startTime.Value.AddSeconds(-5).ToOADate();
			MainChart.ChartAreas[0].AxisX.Maximum = xAxisMax;
		}

		private void ScrollChart(bool toEnd)
		{
			if (!m_startTime.HasValue) return;

			if (toEnd)
			{
				var toValue = MainChart.ChartAreas[0].AxisX.Maximum;
				var toDate = DateTime.FromOADate(toValue);
				var fromValue = toDate.Add(-m_timeFrame).ToOADate();

				MainChart.ChartAreas[0].AxisX.ScaleView.Zoom(fromValue, toValue);
			}
			else
			{
				var frameIndex = MainChartScrollBar.Value;
				var fromValue = m_startTime.Value.AddSeconds(frameIndex / 30f * m_timeFrame.TotalSeconds).ToOADate();
				var toValue = m_startTime.Value.AddSeconds((frameIndex / 30f + 1) * m_timeFrame.TotalSeconds).ToOADate();

				MainChart.ChartAreas[0].AxisX.ScaleView.Zoom(fromValue, toValue);
			}
		}

		private void StartRecording()
		{
			if (m_isRecording) return;

			using (var sf = new SaveFileDialog { Filter = FileFilters.CsvFilter })
			{
				if (sf.ShowDialog() != DialogResult.OK) return;

				var fileName = sf.FileName;
				var ex = Safe.Execute(() =>
				{
					m_fileWriter = new StreamWriter(File.Open(fileName, FileMode.Create, FileAccess.Write, FileShare.Read));
					var header = "Time," + string.Join(",", m_seriesData.Where(x => x.Value.CheckBox.Checked).Select(x => x.Key));
					m_fileWriter.WriteLine(header);
				});
				if (ex != null)
				{
					InfoBox.Show("Unable to start recoding...\n" + ex.Message);
					return;
				}
			}

			m_recordStartTime = DateTime.Now;
			m_isRecording = true;
			m_seriesData.ForEach(x => x.Value.CheckBox.Enabled = false);
			RecordButton.Text = @"Stop Recording";
		}

		private void StopRecording()
		{
			if (!m_isRecording) return;

			Safe.Execute(() =>
			{
				m_fileWriter.Flush();
				m_fileWriter.Dispose();
			});

			m_isRecording = false;
			m_seriesData.ForEach(x => x.Value.CheckBox.Enabled = true);
			RecordButton.Text = @"Record...";
		}

		private void SaveCheckedSeries()
		{
			foreach (var kvp in m_seriesData)
			{
				var seriesName = kvp.Key;
				var data = kvp.Value;
				m_configuration.ActiveSeries[seriesName] = data.CheckBox.Checked;
			}
		}

		private static float Interpolate(float value, IList<ValueLimit<float, int>> lowHigh)
		{
			var low = lowHigh[0];
			var high = lowHigh[1];

			if (value > high.Value) return high.Limit;
			if (value < low.Value) return low.Limit;

			return low.Limit + (value - low.Value) / (high.Value - low.Value) * (high.Limit - low.Limit);
		}

		private void SeriesCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			var checkbox = sender as CheckBox;
			if (checkbox == null || checkbox.Tag == null || string.IsNullOrEmpty(checkbox.Tag.ToString())) return;

			var seriesName = checkbox.Tag.ToString();
			m_seriesData[seriesName].Seires.Enabled = checkbox.Checked;
		}

		private void PuffMenuItem_Click(int seconds)
		{
			if (!EnsureConnection()) return;

			m_connector.MakePuff(seconds);
			PuffButton.Enabled = false;
			new Thread(() =>
			{
				Thread.Sleep(TimeSpan.FromSeconds(seconds));
				UpdateUI(() => PuffButton.Enabled = true);
			}) { IsBackground = true }.Start();
		}

		private class SeriesRelatedData
		{
			private readonly Label m_lastValueLabel;
			private string m_labelFormat;

			public SeriesRelatedData(Color color, CheckBox checkBox, Panel panel, Label lastValueLabel, string labelFormat, [NotNull] ValueLimit<float, int>[] interpolationLimits)
			{
				if (interpolationLimits == null || interpolationLimits.Length != 2) throw new ArgumentNullException("interpolationLimits");

				m_lastValueLabel = lastValueLabel;
				m_labelFormat = labelFormat;

				Color = color;
				CheckBox = checkBox;
				Panel = panel;
				InterpolationLimits = interpolationLimits;
			}

			public Color Color { get; private set; }

			public CheckBox CheckBox { get; private set; }

			public Panel Panel { get; private set; }

			public ValueLimit<float, int>[] InterpolationLimits { get; private set; }

			public Series Seires { get; set; }

			public float? LastValue { get; private set; }

			public void SetLastValueFormat(string format)
			{
				m_labelFormat = format;
			}

			public void SetLastValue(float? value)
			{
				LastValue = value;
				m_lastValueLabel.Text = Seires.Enabled && LastValue.HasValue
					? string.Format(CultureInfo.InvariantCulture, m_labelFormat, LastValue)
					: "?";
			}
		}

		private class ValueLimit<TValue, TLimit>
		{
			public ValueLimit(TValue value, TLimit limit)
			{
				Value = value;
				Limit = limit;
			}

			public TValue Value { get; private set; }

			public TLimit Limit { get; private set; }
		}

		private static class SensorsKeys
		{
			internal const string Timestamp = "Timestamp";

			internal const string IsFiring = "IsFiring";
			internal const string IsCharging = "IsCharging";
			internal const string IsCelcius = "IsCelcius";

			internal const string Battery1 = "Battery1Voltage";
			internal const string Battery2 = "Battery2Voltage";
			internal const string Battery3 = "Battery3Voltage";
			internal const string BatteryPack = "BatteryPack";

			internal const string Power = "Power";
			internal const string PowerSet = "PowerSet";

			internal const string TemperatureSet = "TemperatureSet";
			internal const string Temperature = "Temperature";
			
			internal const string OutputVoltage = "OutputVolts";
			internal const string OutputCurrent = "OutputCurrent";

			internal const string Resistance = "Resistance";
			internal const string RealResistance = "RealResistance";

			internal const string BoardTemperature = "BoardTemperature";
		}
	}
}