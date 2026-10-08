namespace AB9ActiveShifter
{
    public static partial class DefaultProfiles
    {
        /// <summary>
        /// The user's short-throw H tune captured on 2026-10-08: tight slots, a detent
        /// crossover, and its own base resistance. Session and machine facts stay local.
        /// </summary>
        private static ShifterSettings SportShortThrow()
        {
            ShifterSettings s = ShortThrow();
            s.BaseFrictionPct = 35;
            s.DamperCoeff = 3515;
            s.DetentHoldPct = 50;
            s.DetentPullPct = 40;
            s.DetentResistPct = 25;
            s.FloatShiftingEnabled = true;
            s.MouthDepth = 5271;
            s.MouthOpenPct = 81;
            s.NativeEffectsJson = SportShortThrowEffects;
            s.PatternWidthPct = 65;
            s.SlotHalfWidth = 468;
            return s;
        }

        // Keep the complete portable editor tree, including curves and channel settings.
        // This captured tune has no transport or output manager.
        private const string SportShortThrowEffects = @"{
  ""Version"": 1,
  ""GlobalGain"": 100.0,
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
        ""Gain"": 59.0,
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
            ""0;0"",
            ""1;100"",
            ""100;100""
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
        ""Gain"": 72.0,
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
