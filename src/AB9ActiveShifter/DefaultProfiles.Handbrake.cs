using AB9ActiveShifter.Core;

namespace AB9ActiveShifter
{
    public static partial class DefaultProfiles
    {
        /// <summary>
        /// Handbrake (progressive) 4, captured from the user's rig on 2026-10-29.
        /// All portable tuning and the complete native Effects tree are retained.
        /// Session switches, calibration, endpoints and hardware bindings remain local.
        /// </summary>
        private static ShifterSettings Handbrake()
        {
            ShifterSettings s = new ShifterSettings();
            s.ColumnPinForcePct = 100;
            s.DamperCoeff = 1522;
            s.DampingPct = 0;
            s.FxCustomProperty = "wjee";
            s.HandbrakeForceCurve = HandbrakeCurveShape.SmoothS;
            s.HandbrakeForceProgressionPct = 100;
            s.HandbrakeOutputProgressionPct = 100;
            s.HandbrakePullForcePct = 100;
            s.HandbrakeReturnForcePct = 100;
            s.HandbrakeStopForcePct = 100;
            s.HandbrakeTravelPct = 95;
            s.NativeEffectsJson = HandbrakeEffects;
            s.OverallGainPct = 100;
            s.Pattern = GatePattern.Handbrake;
            s.SlotHalfWidth = 844;
            s.WallAttackMs = 15;
            s.WallFrictionPct = 0;
            s.WallRamp = 3815;
            s.WallYieldPct = 0;
            return s;
        }

        // Entire portable tree, including engine and wheel-lock response filters and tones.
        private const string HandbrakeEffects = @"{
  ""Version"": 1,
  ""GlobalGain"": 100.0,
  ""IsMuted"": false,
  ""Profile"": {
    ""CarChoices"": [],
    ""UnmuteEffectsAfterSimhubRestart"": true,
    ""EffectsContainers"": [
      {
        ""ContainerType"": ""AB9GrindEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 60.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""37211ce4-82e3-4037-aaed-1061a28d3f07"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""FilterType"": ""SimpleGammaFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 33,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""AB9BiteEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 35.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""800c49a7-5904-4369-bc79-9686df5f56ae"",
        ""Filter"": {
          ""Duration"": 60,
          ""FilterType"": ""PulseFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 50,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""RPMContainer"",
        ""IsEnabled"": true,
        ""Gain"": 25.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""44aed9ea-575d-4dce-b92a-7ef0ed508b15"",
        ""Filter"": {
          ""ControlPoints"": [
            ""0;0"",
            ""1;100"",
            ""100;100""
          ],
          ""CurveFitting"": 0,
          ""FilterType"": ""SplineFilter""
        },
        ""Output"": {
          ""UseHighFrequency"": true,
          ""HighFrequency"": 119,
          ""WhiteNoise"": 40,
          ""UseWhiteNoise"": false,
          ""FrequencyBasedOnPreFilter"": true,
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 17,
          ""OutputType"": ""ToneOutput""
        }
      },
      {
        ""ContainerType"": ""AB9LimiterEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 45.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""3af57429-940e-4535-9ef2-418a63485251"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""FilterType"": ""SimpleGammaFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 55,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""ABSActiveEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 40.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""2ee284d2-e972-44d1-8e85-9f30002980dd"",
        ""Filter"": {
          ""Duration"": 0,
          ""FilterType"": ""PulseFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 44,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""TCActiveEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 35.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""9855e887-5f7b-4858-8d6f-d946119ecfe9"",
        ""Filter"": {
          ""Duration"": 0,
          ""FilterType"": ""PulseFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 60,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""WheelsImpactContainer"",
        ""IsEnabled"": false,
        ""Gain"": 45.0,
        ""AutocalibrationMin"": 50.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""FrontLeft"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""FrontRight"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""RearLeft"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""RearRight"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""99a51c1b-cdcf-4c8a-bafc-ce4e7e62de81"",
        ""AggregationMode"": ""Corners"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""InputGain"": 100.0,
          ""MinimumForce"": 0,
          ""Threshold"": 0,
          ""FilterType"": ""GammaFilter""
        },
        ""Output"": {
          ""UseHighFrequency"": false,
          ""HighFrequency"": 50,
          ""WhiteNoise"": 10,
          ""UseWhiteNoise"": false,
          ""FrequencyBasedOnPreFilter"": false,
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 40,
          ""OutputType"": ""ToneOutput""
        }
      },
      {
        ""ContainerType"": ""GearEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 45.0,
        ""ModulateGainUsingRpms"": false,
        ""MaxFeedbackRpmPercent"": 90,
        ""MinFeedbackRpmPercent"": 50,
        ""GearMode"": 2,
        ""AlwaysIgnoreNeutral"": false,
        ""IgnoreNeutral"": true,
        ""NeutralDebouningTime"": 200.0,
        ""EngagingDebouningTime"": 100.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""61c2d164-2f59-43fc-8030-73e76e559696"",
        ""Filter"": {
          ""Duration"": 80,
          ""FilterType"": ""PulseFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 44,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""AB9PropertyEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 100.0,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""All"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""c24aad38-ec3f-4c85-8ed7-6e5290293938"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""FilterType"": ""SimpleGammaFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 44,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""WheelsLockContainer"",
        ""IsEnabled"": true,
        ""Gain"": 100.0,
        ""IsLock"": true,
        ""UseLegacyIracingAlgorythm"": false,
        ""LockSensibility"": 10.0,
        ""BrakeFilter"": 20,
        ""SettingsStore"": {
          ""Settings"": [
            {
              ""Channels"": {
                ""FrontLeft"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""FrontRight"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""RearLeft"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""RearRight"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                },
                ""Front"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": false
                    }
                  }
                },
                ""Rear"": {
                  ""Channels"": {
                    ""0"": {
                      ""IsEnabled"": true
                    }
                  }
                }
              },
              ""TypeName"": ""DeviceChannelActivationSettings""
            }
          ]
        },
        ""ContainerId"": ""73b9d624-a474-463e-ac6c-46ac9c235295"",
        ""AggregationMode"": ""Front/Rear"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""InputGain"": 100.0,
          ""MinimumForce"": 0,
          ""Threshold"": 0,
          ""FilterType"": ""GammaFilter""
        },
        ""Output"": {
          ""UseHighFrequency"": false,
          ""HighFrequency"": 50,
          ""WhiteNoise"": 10,
          ""UseWhiteNoise"": true,
          ""FrequencyBasedOnPreFilter"": false,
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 20,
          ""OutputType"": ""ToneOutput""
        }
      }
    ],
    ""AutoCalibrationRatio2"": 100,
    ""OutputMode"": 3,
    ""GlobalGain"": 50.0,
    ""UseProfileGain"": false,
    ""LastLoaded"": ""0001-01-01T00:00:00"",
    ""Name"": ""Lever effects"",
    ""ProfileId"": ""dcd900ad-9650-46fc-bb7c-f8cc72d8f234"",
    ""GameCode"": null,
    ""CarChoice"": null
  }
}";
    }
}
