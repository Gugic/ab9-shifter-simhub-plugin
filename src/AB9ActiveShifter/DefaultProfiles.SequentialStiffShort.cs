namespace AB9ActiveShifter
{
    public static partial class DefaultProfiles
    {
        /// <summary>
        /// The custom Sequential tune captured on the rig: a short stroke, full click,
        /// and native effects. Presets applies the shared base damping/friction afterward.
        /// Calibration and session state stay at defaults.
        /// </summary>
        private static ShifterSettings SequentialStiffShort()
        {
            ShifterSettings s = Sequential();
            s.DetentResistPct = 46;
            s.EngageDepth = 25215;
            s.FxLimiterEnabled = true;
            s.ReleaseDepth = 25715;
            s.SeqClickPct = 100;
            s.NativeEffectsJson = SequentialStiffShortEffects;
            return s;
        }

        // Preserve the native editor's complete portable tree rather than migrating its
        // curves and effect options through the older scalar dials. No transport is stored.
        private const string SequentialStiffShortEffects = @"{
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
        ""ContainerId"": ""579865a1-664d-4bb9-85fd-b5db918f158f"",
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
        ""ContainerId"": ""c573a537-415f-4c4b-ae57-6a5fadd01002"",
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
        ""ContainerId"": ""20a34d1b-8476-4220-9de8-930c78785b1b"",
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
          ""HighFrequency"": 98,
          ""WhiteNoise"": 40,
          ""UseWhiteNoise"": false,
          ""FrequencyBasedOnPreFilter"": true,
          ""UsePrehemptiveMode"": false,
          ""Frequency"": 14,
          ""OutputType"": ""ToneOutput""
        }
      },
      {
        ""ContainerType"": ""AB9LimiterEffectContainer"",
        ""IsEnabled"": true,
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
        ""ContainerId"": ""9af9a55f-6022-4056-bd79-97e8145bbb5d"",
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
        ""ContainerId"": ""e3de6e8f-b4b4-4425-a441-d7c3b31da0df"",
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
        ""ContainerId"": ""c4b71514-0a31-4c2a-8769-bdf68d37af1e"",
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
        ""Gain"": 64.0,
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
        ""ContainerId"": ""cfd70600-db26-4ad7-9e6c-091b90e95837"",
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
        ""Gain"": 100.0,
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
        ""ContainerId"": ""d821ea98-fcb2-4f41-a754-495191c60ebc"",
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
        ""ContainerId"": ""be93a6ea-ac72-463d-93a5-07cfccf7a221"",
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
    ""ProfileId"": ""12e4ae44-fdd7-4442-a165-e88edf14fca8"",
    ""GameCode"": null,
    ""CarChoice"": null
  }
}";
    }
}
