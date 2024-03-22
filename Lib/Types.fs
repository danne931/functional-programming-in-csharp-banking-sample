module Lib.Types

open System

type StreamThrottle = {
   Count: int
   Burst: int
   Duration: TimeSpan
}

type StreamChunking = { Size: int; Duration: TimeSpan }

type BackoffSupervisorOptions = {
   MinBackoff: TimeSpan
   MaxBackoff: TimeSpan
   RandomFactor: float
   MaxNrOfRetries: int
   ResetCounterAfter: TimeSpan
}

type PersistenceSupervisorOptions = {
   MinBackoff: TimeSpan
   MaxBackoff: TimeSpan
   RandomFactor: float
   MaxNrOfRetries: int
}
