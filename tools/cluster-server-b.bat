@REM https://docs.nats.io/running-a-nats-service/introduction/flags
@REM -DV is Debug and Protocol Trace
@REM -DVV is Debug and Verbose
nats-server.exe -D -p 5248 -cluster nats://localhost:5248 -routes nats://localhost:4248 --cluster_name test-cluster