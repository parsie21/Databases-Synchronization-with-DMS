-- restore-dq.sql

RESTORE DATABASE [NaturaSi]
FROM DISK = "C:\sorgenti\docker_test\test_sync\init\4_NATURASI.bak"
WITH MOVE 'NaturaSi' TO 'C:\sorgenti\docker_test\test_sync\data\NaturaSi.mdf',
     MOVE 'NaturaSi_log' TO 'C:\sorgenti\docker_test\test_sync\data\NaturaSi_log.ldf',
     REPLACE,
     STATS = 5; -- stats means show progress every 5 percent
GO



