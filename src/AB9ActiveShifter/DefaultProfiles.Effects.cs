namespace AB9ActiveShifter
{
    public static partial class DefaultProfiles
    {
        // The complete portable Effects tune from "5+R (narrow, short throw, stiff, sport) 3",
        // captured on 2026-11-03. All shifter presets share it; Handbrake keeps its rear-wheel
        // lock tune. Preserve the master volume, row gains, curves, filters and channel routing.
        private const string BalancedShifterEffects = @"{
  ""Version"": 1,
  ""GlobalGain"": 80.000000000000014,
  ""IsMuted"": false,
  ""Profile"": {
    ""CarChoices"": [],
    ""UnmuteEffectsAfterSimhubRestart"": true,
    ""EffectsContainers"": [
      {
        ""ContainerType"": ""AB9GrindEffectContainer"",
        ""IsEnabled"": true,
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
        ""ContainerId"": ""dea4a125-0b0f-40ce-89fa-e8a6713fb4e8"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""FilterType"": ""SimpleGammaFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 15,
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
        ""ContainerId"": ""d74a8b3a-8ff8-4102-a75b-2bafb46a1ac9"",
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
        ""Gain"": 79.952380952380935,
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
        ""ContainerId"": ""9a26583d-95c1-41fe-a9c5-f6b8a0c02c15"",
        ""Filter"": {
          ""ControlPoints"": [
            ""7.45562130177515;22.6688974647061"",
            ""10.887573964497;26.811342875287"",
            ""29.5857988165681;99.821943236774"",
            ""53.7278106508876;100"",
            ""35.5029585798817;100"",
            ""78.2248520710059;100"",
            ""16.3313609467456;30.1770797713839"",
            ""22.9585798816568;63.8344487323531""
          ],
          ""CurveFitting"": 0,
          ""FilterType"": ""SplineFilter""
        },
        ""Output"": {
          ""UseHighFrequency"": true,
          ""HighFrequency"": 84,
          ""WhiteNoise"": 40,
          ""UseWhiteNoise"": false,
          ""FrequencyBasedOnPreFilter"": true,
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 12,
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
        ""ContainerId"": ""b611892b-9170-428d-bf6e-2982609ae478"",
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
        ""ContainerId"": ""80e2b153-6109-4290-8ab3-1a423af84a14"",
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
        ""ContainerId"": ""d7acbdcb-9c54-40b6-b6c8-611ad54cff0f"",
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
        ""IsEnabled"": true,
        ""Gain"": 100.0,
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
        ""ContainerId"": ""653b13dc-1dc9-48f0-996b-b101d526865a"",
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
        ""IsEnabled"": true,
        ""Gain"": 79.619047619047564,
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
        ""ContainerId"": ""52726e11-fbd3-47b2-a56b-eba44ee33afd"",
        ""Filter"": {
          ""Duration"": 107,
          ""FilterType"": ""PulseFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 37,
          ""OutputType"": ""SingleToneOutput""
        }
      },
      {
        ""ContainerType"": ""AB9PropertyEffectContainer"",
        ""IsEnabled"": false,
        ""Gain"": 30.0,
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
        ""ContainerId"": ""bb0c2106-64a8-4f13-8453-57bf3a83fbb5"",
        ""Filter"": {
          ""GammaValue"": 1.0,
          ""FilterType"": ""SimpleGammaFilter""
        },
        ""Output"": {
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 44,
          ""OutputType"": ""SingleToneOutput""
        }
      }
    ],
    ""AutoCalibrationRatio2"": 100,
    ""OutputMode"": 3,
    ""GlobalGain"": 50.0,
    ""UseProfileGain"": false,
    ""LastLoaded"": ""0001-01-01T00:00:00"",
    ""Name"": ""Lever effects"",
    ""ProfileId"": ""57364152-41e2-44b6-af13-148f6d968408"",
    ""GameCode"": null,
    ""CarChoice"": null
  }
}";
    }
}
