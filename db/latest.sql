-- MySQL dump 10.13  Distrib 8.0.44, for Win64 (x86_64)
--
-- Host: localhost    Database: jobportal
-- ------------------------------------------------------
-- Server version	8.0.44

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Table structure for table `admin_log`
--

DROP TABLE IF EXISTS `admin_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `admin_log` (
  `log_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `action_type` varchar(80) COLLATE utf8mb4_unicode_ci NOT NULL,
  `timestamp` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`log_id`),
  KEY `fk_adminlog_user` (`user_id`),
  CONSTRAINT `fk_adminlog_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `admin_log`
--

LOCK TABLES `admin_log` WRITE;
/*!40000 ALTER TABLE `admin_log` DISABLE KEYS */;
INSERT INTO `admin_log` VALUES (1,1,'System Initialization','2025-10-29 22:51:15'),(2,2,'Reviewed job posting approvals','2025-10-29 22:51:15'),(3,1,'Monitored recruiter activity','2025-10-29 22:51:15'),(4,2,'Updated notification settings','2025-10-29 22:51:15'),(5,1,'Viewed flagged conversations','2025-10-29 22:51:15'),(6,2,'Checked AI analysis summaries','2025-10-29 22:51:15'),(7,1,'Exported user records','2025-10-29 22:51:15'),(8,2,'Modified resume review rules','2025-10-29 22:51:15'),(9,1,'Invited recruiter to compliance review','2025-10-29 22:51:15'),(10,2,'Cleared stale logs','2025-10-29 22:51:15'),(11,1,'Ran daily audit tasks','2025-10-29 22:51:15'),(12,2,'Reviewed message reports','2025-10-29 22:51:15'),(13,1,'Checked application pipeline flow','2025-10-29 22:51:15'),(14,2,'Processed system cleanup','2025-10-29 22:51:15'),(15,1,'Enabled system monitoring trigger','2025-10-29 22:51:15'),(16,2,'Analyzed message sentiment data','2025-10-29 22:51:15'),(17,1,'Checked notification queue','2025-10-29 22:51:15'),(18,2,'Performed backup validation','2025-10-29 22:51:15'),(19,1,'Reviewed data integrity checks','2025-10-29 22:51:15'),(20,2,'Completed scheduled maintenance','2025-10-29 22:51:15');
/*!40000 ALTER TABLE `admin_log` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ai_resume_analysis`
--

DROP TABLE IF EXISTS `ai_resume_analysis`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `ai_resume_analysis` (
  `analysis_id` int NOT NULL AUTO_INCREMENT,
  `resume_id` int NOT NULL,
  `grammar_score` tinyint unsigned DEFAULT NULL,
  `formatting_score` tinyint unsigned DEFAULT NULL,
  `completeness_score` tinyint unsigned DEFAULT NULL,
  `suggestions` text COLLATE utf8mb4_unicode_ci,
  PRIMARY KEY (`analysis_id`),
  KEY `fk_analysis_resume` (`resume_id`),
  CONSTRAINT `fk_analysis_resume` FOREIGN KEY (`resume_id`) REFERENCES `resume` (`resume_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ai_resume_analysis`
--

LOCK TABLES `ai_resume_analysis` WRITE;
/*!40000 ALTER TABLE `ai_resume_analysis` DISABLE KEYS */;
INSERT INTO `ai_resume_analysis` VALUES (1,1,78,72,75,'Add clearer job role descriptions.'),(2,2,82,77,80,'Improve layout consistency.'),(3,3,85,81,82,'Strong education background. Add projects.'),(4,4,88,86,83,'Good formatting; highlight achievements.'),(5,5,91,87,89,'Very strong resume.'),(6,6,75,70,72,'Expand work experience details.'),(7,7,80,77,79,'Consider adding leadership examples.'),(8,8,84,79,81,'Good structure, minor tweaks suggested.'),(9,9,70,65,68,'Add technical skill keywords.'),(10,10,73,68,70,'Improve work history bullet clarity.'),(11,11,90,88,86,'Strong resume with clear progression.'),(12,12,82,76,78,'Consider adding certification section.'),(13,13,77,71,74,'Add project impact statements.'),(14,14,79,75,76,'Formatting good; expand achievements.'),(15,15,88,82,85,'Solid; highlight portfolio links.'),(16,16,74,69,71,'Add more industry keywords.'),(17,17,93,90,88,'Excellent technical portfolio.'),(18,18,86,82,84,'Very strong project section.'),(19,19,72,67,70,'Improve clarity in job role summaries.'),(20,20,89,86,87,'Well structured and professional.');
/*!40000 ALTER TABLE `ai_resume_analysis` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ai_resume_evaluation`
--

DROP TABLE IF EXISTS `ai_resume_evaluation`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `ai_resume_evaluation` (
  `evaluation_id` int NOT NULL AUTO_INCREMENT,
  `job_listing_id` int NOT NULL,
  `resume_id` int NOT NULL,
  `match_score` tinyint unsigned DEFAULT NULL,
  PRIMARY KEY (`evaluation_id`),
  KEY `fk_eval_job` (`job_listing_id`),
  KEY `fk_eval_resume` (`resume_id`),
  CONSTRAINT `fk_eval_job` FOREIGN KEY (`job_listing_id`) REFERENCES `job_listing` (`job_listing_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_eval_resume` FOREIGN KEY (`resume_id`) REFERENCES `resume` (`resume_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ai_resume_evaluation`
--

LOCK TABLES `ai_resume_evaluation` WRITE;
/*!40000 ALTER TABLE `ai_resume_evaluation` DISABLE KEYS */;
INSERT INTO `ai_resume_evaluation` VALUES (1,1,1,76),(2,2,2,81),(3,3,3,84),(4,4,4,79),(5,5,5,73),(6,6,6,68),(7,7,7,83),(8,8,8,71),(9,9,9,87),(10,10,10,74),(11,11,11,80),(12,12,12,77),(13,13,13,85),(14,14,14,69),(15,15,15,82),(16,16,16,75),(17,17,17,89),(18,18,18,78),(19,19,19,72),(20,20,20,86);
/*!40000 ALTER TABLE `ai_resume_evaluation` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `company`
--

DROP TABLE IF EXISTS `company`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `company` (
  `company_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `company_name` varchar(160) COLLATE utf8mb4_unicode_ci NOT NULL,
  `company_industry` varchar(120) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `company_location` varchar(120) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `company_description` text COLLATE utf8mb4_unicode_ci,
  `company_status` enum('Active','Pending','Suspended') COLLATE utf8mb4_unicode_ci DEFAULT 'Active',
  PRIMARY KEY (`company_id`),
  KEY `fk_company_user` (`user_id`),
  KEY `idx_company_name` (`company_name`),
  CONSTRAINT `fk_company_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `company`
--

LOCK TABLES `company` WRITE;
/*!40000 ALTER TABLE `company` DISABLE KEYS */;
INSERT INTO `company` VALUES (1,3,'TalentAce Recruitment','Recruitment','Shah Alam','Regional recruitment & staffing services','Active'),(2,3,'TalentAce TechHire','HR Tech','Petaling Jaya','Specialises in tech recruitment solutions','Active'),(3,4,'CodeWave Sdn Bhd','Software','Cyberjaya','Software outsourcing & web application development','Active'),(4,4,'CodeWave Labs','Consulting','Kuala Lumpur','R&D and enterprise solutions','Active'),(5,5,'TechForge Solutions','IT Services','Penang','Full-stack development and digital transformation','Active'),(6,5,'TechForge Cloud','Cloud Services','George Town','Cloud migration & managed services','Active'),(7,6,'PixelHub Studio','Design & UX','Kuala Lumpur','Product design and UX for startups','Active'),(8,6,'PixelHub Interactive','Media','Kuala Lumpur','Interactive media and front-end experiences','Active'),(9,7,'NovaLabs','Software','Johor Bahru','AI & data analytics startup','Active'),(10,7,'NovaLabs Solutions','Enterprise','Johor Bahru','Enterprise AI integrations','Active'),(11,8,'RecruitNow Sdn Bhd','Recruitment','Ipoh','Local recruitment firm focusing on SMEs','Active'),(12,8,'RecruitNow Tech','Tech Recruitment','Ipoh','Specialised tech hiring','Active'),(13,9,'RocketForge','DevOps','Kota Kinabalu','DevOps consultancy and infrastructure','Active'),(14,9,'RocketForge Labs','SRE','Kota Kinabalu','Site reliability and automation services','Active'),(15,10,'HRSmart Malaysia','HR Software','Kuala Lumpur','HR SaaS platform for SMEs','Active'),(16,10,'HRSmart Insights','Data','Kuala Lumpur','People analytics & dashboards','Active'),(17,3,'AceTemp Solutions','Staffing','Penang','Temporary staffing and payroll services','Active'),(18,4,'WaveTech Integrations','Integration','Shah Alam','API integrations and middleware services','Active'),(19,5,'ForgeEdge','Security','Cyberjaya','Application security and penetration testing','Active'),(20,6,'PixelWorks Studio','Product','Petaling Jaya','MVPs & startup product builds','Active');
/*!40000 ALTER TABLE `company` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `conversation`
--

DROP TABLE IF EXISTS `conversation`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `conversation` (
  `conversation_id` int NOT NULL AUTO_INCREMENT,
  `job_listing_id` int NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_message_at` datetime DEFAULT NULL,
  `last_snippet` varchar(200) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `unread_for_recruiter` int NOT NULL DEFAULT '0',
  `unread_for_candidate` int NOT NULL DEFAULT '0',
  `recruiter_id` int DEFAULT NULL,
  `candidate_id` int DEFAULT NULL,
  `job_title` varchar(200) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `candidate_name` varchar(200) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`conversation_id`),
  KEY `fk_conversation_job` (`job_listing_id`),
  KEY `idx_conv_last_at` (`last_message_at`),
  KEY `idx_conv_recruiter` (`recruiter_id`,`last_message_at`),
  CONSTRAINT `fk_conversation_job` FOREIGN KEY (`job_listing_id`) REFERENCES `job_listing` (`job_listing_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `conversation`
--

LOCK TABLES `conversation` WRITE;
/*!40000 ALTER TABLE `conversation` DISABLE KEYS */;
INSERT INTO `conversation` VALUES (1,1,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. Are you available for a short call this week?',0,0,3,NULL,'Junior HR Coordinator',NULL),(2,2,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for your application. May we schedule an intro discussion?',0,0,3,NULL,'Technical Recruiter',NULL),(3,3,'2025-10-29 22:51:15','2025-10-29 22:51:15','We have received your application. Are you free for a brief call tomorrow?',0,0,4,NULL,'Backend .NET Developer',NULL),(4,4,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. Could we arrange a preliminary interview?',0,0,4,NULL,'Frontend React Developer',NULL),(5,5,'2025-10-29 22:51:15','2025-10-29 22:51:15','We appreciate your application. Are you open for a short discussion this week?',0,0,5,NULL,'IT Support Specialist',NULL),(6,6,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for your interest. May we schedule an online interview session?',0,0,5,NULL,'Cloud System Engineer',NULL),(7,7,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. Are you available for an interview this week?',0,0,6,NULL,'UI/UX Designer',NULL),(8,8,'2025-10-29 22:51:15','2025-10-29 22:51:15','We have reviewed your application. When are you free for a discussion?',0,0,6,NULL,'Graphic Illustrator',NULL),(9,9,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. May we arrange a short intro call?',0,0,7,NULL,'AI Data Analyst',NULL),(10,10,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for your application. Are you available for a call soon?',0,0,7,NULL,'Machine Learning Engineer',NULL),(11,11,'2025-10-29 22:51:15','2025-10-29 22:51:15','We have received your application. Can we schedule a quick discussion?',0,0,8,NULL,'Recruitment Consultant',NULL),(12,12,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. Are you open for a call this week?',0,0,8,NULL,'Talent Acquisition Lead',NULL),(13,13,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for your interest. When can we arrange a short call?',0,0,9,NULL,'DevOps Engineer',NULL),(14,14,'2025-10-29 22:51:15','2025-10-29 22:51:15','We would like to discuss your application. Are you available soon?',0,0,9,NULL,'SRE Engineer',NULL),(15,15,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. May we set an interview schedule?',0,0,10,NULL,'HR System Analyst',NULL),(16,16,'2025-10-29 22:51:15','2025-10-29 22:51:15','We have reviewed your profile. Can we arrange a call?',0,0,10,NULL,'HR Data Specialist',NULL),(17,17,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for applying. Are you available for a call this week?',0,0,3,NULL,'Contract Recruiter',NULL),(18,18,'2025-10-29 22:51:15','2025-10-29 22:51:15','We appreciate your application. May we schedule an interview?',0,0,4,NULL,'API Integration Developer',NULL),(19,19,'2025-10-29 22:51:15','2025-10-29 22:51:15','Thank you for your interest. When are you free for a call?',0,0,5,NULL,'Cybersecurity Analyst',NULL),(20,20,'2025-10-29 22:51:15','2025-10-29 22:51:15','We would like to discuss your application soon. Are you available?',0,0,6,NULL,'Product UI Developer',NULL);
/*!40000 ALTER TABLE `conversation` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `conversation_monitor`
--

DROP TABLE IF EXISTS `conversation_monitor`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `conversation_monitor` (
  `monitor_id` int NOT NULL AUTO_INCREMENT,
  `conversation_id` int NOT NULL,
  `user_id` int NOT NULL,
  `flag` tinyint(1) NOT NULL DEFAULT '0',
  `date_reviewed` datetime DEFAULT NULL,
  PRIMARY KEY (`monitor_id`),
  KEY `fk_monitor_conv` (`conversation_id`),
  KEY `fk_monitor_user` (`user_id`),
  CONSTRAINT `fk_monitor_conv` FOREIGN KEY (`conversation_id`) REFERENCES `conversation` (`conversation_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_monitor_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `conversation_monitor`
--

LOCK TABLES `conversation_monitor` WRITE;
/*!40000 ALTER TABLE `conversation_monitor` DISABLE KEYS */;
INSERT INTO `conversation_monitor` VALUES (1,1,1,0,NULL),(2,2,2,0,NULL),(3,3,1,0,NULL),(4,4,2,0,NULL),(5,5,1,0,NULL),(6,6,2,0,NULL),(7,7,1,0,NULL),(8,8,2,0,NULL),(9,9,1,0,NULL),(10,10,2,0,NULL),(11,11,1,0,NULL),(12,12,2,0,NULL),(13,13,1,0,NULL),(14,14,2,0,NULL),(15,15,1,0,NULL),(16,16,2,0,NULL),(17,17,1,0,NULL),(18,18,2,0,NULL),(19,19,1,0,NULL),(20,20,2,0,NULL);
/*!40000 ALTER TABLE `conversation_monitor` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `job_application`
--

DROP TABLE IF EXISTS `job_application`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `job_application` (
  `application_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `job_listing_id` int NOT NULL,
  `application_status` enum('Submitted','AI-Screened','Shortlisted','Interview','Offer','Hired','Rejected') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Submitted',
  `date_updated` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`application_id`),
  KEY `ix_application_user` (`user_id`),
  KEY `ix_application_job` (`job_listing_id`),
  KEY `ix_app_job_status` (`job_listing_id`,`application_status`),
  CONSTRAINT `fk_application_job` FOREIGN KEY (`job_listing_id`) REFERENCES `job_listing` (`job_listing_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_application_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `job_application`
--

LOCK TABLES `job_application` WRITE;
/*!40000 ALTER TABLE `job_application` DISABLE KEYS */;
INSERT INTO `job_application` VALUES (1,11,1,'Submitted','2025-10-29 23:10:56'),(2,12,2,'Submitted','2025-10-29 23:10:56'),(3,13,3,'Submitted','2025-10-29 23:10:56'),(4,14,4,'Submitted','2025-10-29 23:10:56'),(5,15,5,'Submitted','2025-10-29 23:10:56'),(6,16,6,'Submitted','2025-10-29 23:10:56'),(7,17,7,'Submitted','2025-10-29 23:10:56'),(8,18,8,'Submitted','2025-10-29 23:10:56'),(9,19,9,'Submitted','2025-10-29 23:10:56'),(10,20,10,'Submitted','2025-10-29 23:10:56'),(11,11,11,'Submitted','2025-10-29 22:51:15'),(12,12,12,'Submitted','2025-10-29 22:51:15'),(13,13,13,'Submitted','2025-10-29 22:51:15'),(14,14,14,'Submitted','2025-10-29 22:51:15'),(15,15,15,'Submitted','2025-10-29 22:51:15'),(16,16,16,'Submitted','2025-10-29 22:51:15'),(17,17,17,'Submitted','2025-10-29 22:51:15'),(18,18,18,'Submitted','2025-10-29 22:51:15'),(19,19,19,'Submitted','2025-10-29 22:51:15'),(20,20,20,'Submitted','2025-10-29 22:51:15');
/*!40000 ALTER TABLE `job_application` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `job_listing`
--

DROP TABLE IF EXISTS `job_listing`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `job_listing` (
  `job_listing_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `company_id` int NOT NULL,
  `job_title` varchar(160) COLLATE utf8mb4_unicode_ci NOT NULL,
  `job_description` text COLLATE utf8mb4_unicode_ci,
  `job_requirements` text COLLATE utf8mb4_unicode_ci,
  `salary_min` decimal(10,2) DEFAULT NULL,
  `salary_max` decimal(10,2) DEFAULT NULL,
  `job_status` enum('Draft','Open','Paused','Closed') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Draft',
  `date_posted` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`job_listing_id`),
  KEY `ix_joblisting_company` (`company_id`),
  KEY `ix_joblisting_user` (`user_id`),
  CONSTRAINT `fk_joblisting_company` FOREIGN KEY (`company_id`) REFERENCES `company` (`company_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_joblisting_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `job_listing`
--

LOCK TABLES `job_listing` WRITE;
/*!40000 ALTER TABLE `job_listing` DISABLE KEYS */;
INSERT INTO `job_listing` VALUES (1,3,1,'Junior HR Coordinator','Assist in candidate sourcing and screening.','Communication, Coordination, MS Office',2500.00,3500.00,'Open','2025-10-29 22:51:15'),(2,3,2,'Technical Recruiter','Recruit IT talent for various clients.','Recruitment, Sourcer Tools, Interviews',3500.00,5500.00,'Open','2025-10-29 22:51:15'),(3,4,3,'Backend .NET Developer','Maintain .NET APIs for enterprise apps.','.NET Core, SQL Server, Git',4000.00,6000.00,'Open','2025-10-29 22:51:15'),(4,4,4,'Frontend React Developer','Build responsive UIs.','React, Tailwind, REST APIs',3800.00,5500.00,'Open','2025-10-29 22:51:15'),(5,5,5,'IT Support Specialist','Provide first-line support.','Windows, Networking, Troubleshooting',2800.00,4200.00,'Open','2025-10-29 22:51:15'),(6,5,6,'Cloud System Engineer','Manage cloud infra deployments.','AWS/Azure, CI/CD, Linux',5500.00,8500.00,'Draft','2025-10-29 22:51:15'),(7,6,7,'UI/UX Designer','Design web and mobile interfaces.','Figma, User Testing, Prototyping',3500.00,5200.00,'Open','2025-10-29 22:51:15'),(8,6,8,'Graphic Illustrator','Create brand identity assets.','Illustrator, Photoshop, Branding',2500.00,4000.00,'Open','2025-10-29 22:51:15'),(9,7,9,'AI Data Analyst','Analyze datasets for ML models.','Python, SQL, Data Visualization',4200.00,6500.00,'Open','2025-10-29 22:51:15'),(10,7,10,'Machine Learning Engineer','Develop ML pipelines.','TensorFlow, PyTorch, AWS',6000.00,9500.00,'Draft','2025-10-29 22:51:15'),(11,8,11,'Recruitment Consultant','Manage recruitment pipelines.','Sourcing, Candidate Management',3000.00,5000.00,'Open','2025-10-29 22:51:15'),(12,8,12,'Talent Acquisition Lead','Lead hiring strategy.','Leadership, Recruitment Strategy',6000.00,9000.00,'Open','2025-10-29 22:51:15'),(13,9,13,'DevOps Engineer','Automate infra & pipelines.','Docker, Kubernetes, Linux',5500.00,9000.00,'Open','2025-10-29 22:51:15'),(14,9,14,'SRE Engineer','Maintain high uptime systems.','Monitoring, Kubernetes, Python',6500.00,10500.00,'Draft','2025-10-29 22:51:15'),(15,10,15,'HR System Analyst','Support clients using HR SaaS.','SQL, HRIS, Documentation',3500.00,5500.00,'Open','2025-10-29 22:51:15'),(16,10,16,'HR Data Specialist','Analyze HR metrics.','Excel, PowerBI, Reporting',4000.00,6500.00,'Open','2025-10-29 22:51:15'),(17,3,17,'Contract Recruiter','Short-term recruitment sourcing.','Sourcing Tools, Interviewing',3000.00,4800.00,'Open','2025-10-29 22:51:15'),(18,4,18,'API Integration Developer','Develop integration middleware.','Node.js, REST, SQL',4500.00,7000.00,'Open','2025-10-29 22:51:15'),(19,5,19,'Cybersecurity Analyst','Monitor and secure infra.','SIEM, SOC, Linux Security',5500.00,8000.00,'Open','2025-10-29 22:51:15'),(20,6,20,'Product UI Developer','Build UI for SaaS products.','Vue/React, HTML/CSS, UX basics',3800.00,5500.00,'Open','2025-10-29 22:51:15');
/*!40000 ALTER TABLE `job_listing` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `job_offer`
--

DROP TABLE IF EXISTS `job_offer`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `job_offer` (
  `offer_id` int NOT NULL AUTO_INCREMENT,
  `application_id` int NOT NULL,
  `offer_status` enum('Draft','Sent','Accepted','Declined','Withdrawn','Expired') COLLATE utf8mb4_unicode_ci DEFAULT 'Draft',
  `salary_offer` decimal(12,2) DEFAULT NULL,
  `start_date` date DEFAULT NULL,
  `contract_type` varchar(60) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `notes` text COLLATE utf8mb4_unicode_ci,
  `candidate_token` char(36) COLLATE utf8mb4_unicode_ci NOT NULL,
  `date_sent` datetime DEFAULT NULL,
  `date_updated` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`offer_id`),
  UNIQUE KEY `uq_offer_token` (`candidate_token`),
  KEY `ix_offer_app` (`application_id`),
  KEY `ix_offer_status` (`offer_status`),
  CONSTRAINT `fk_offer_app` FOREIGN KEY (`application_id`) REFERENCES `job_application` (`application_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `job_offer`
--

LOCK TABLES `job_offer` WRITE;
/*!40000 ALTER TABLE `job_offer` DISABLE KEYS */;
/*!40000 ALTER TABLE `job_offer` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `job_post_approval`
--

DROP TABLE IF EXISTS `job_post_approval`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `job_post_approval` (
  `approval_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `job_listing_id` int NOT NULL,
  `approval_status` enum('Pending','Approved','ChangesRequested','Rejected') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Pending',
  `comments` text COLLATE utf8mb4_unicode_ci,
  `date_approved` datetime DEFAULT NULL,
  PRIMARY KEY (`approval_id`),
  KEY `fk_approval_admin` (`user_id`),
  KEY `fk_approval_job` (`job_listing_id`),
  CONSTRAINT `fk_approval_admin` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT `fk_approval_job` FOREIGN KEY (`job_listing_id`) REFERENCES `job_listing` (`job_listing_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `job_post_approval`
--

LOCK TABLES `job_post_approval` WRITE;
/*!40000 ALTER TABLE `job_post_approval` DISABLE KEYS */;
INSERT INTO `job_post_approval` VALUES (1,1,1,'Pending','Awaiting HR review',NULL),(2,2,2,'Pending','Pending internal check',NULL),(3,1,3,'Pending','Awaiting approval',NULL),(4,2,4,'Pending','Pending review',NULL),(5,1,5,'Pending','Awaiting details',NULL),(6,2,6,'Pending','Pending screening',NULL),(7,1,7,'Pending','Awaiting review',NULL),(8,2,8,'Pending','Pending approval',NULL),(9,1,9,'Pending','Awaiting checks',NULL),(10,2,10,'Pending','Requires admin review',NULL),(11,1,11,'Pending','Pending authorization',NULL),(12,2,12,'Pending','Awaiting assignment',NULL),(13,1,13,'Pending','Pending discussion',NULL),(14,2,14,'Pending','Awaiting approval flow',NULL),(15,1,15,'Pending','Requires decision',NULL),(16,2,16,'Pending','Pending validation',NULL),(17,1,17,'Pending','Awaiting final review',NULL),(18,2,18,'Pending','Pending verification',NULL),(19,1,19,'Pending','Awaiting HR approval',NULL),(20,2,20,'Pending','Awaiting review cycle',NULL);
/*!40000 ALTER TABLE `job_post_approval` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `job_seeker_note`
--

DROP TABLE IF EXISTS `job_seeker_note`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `job_seeker_note` (
  `note_id` int NOT NULL AUTO_INCREMENT,
  `job_seeker_id` int NOT NULL,
  `job_recruiter_id` int NOT NULL,
  `application_id` int NOT NULL,
  `note_text` text COLLATE utf8mb4_unicode_ci NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`note_id`),
  KEY `fk_note_seeker` (`job_seeker_id`),
  KEY `fk_note_recruiter` (`job_recruiter_id`),
  KEY `fk_note_app` (`application_id`),
  CONSTRAINT `fk_note_app` FOREIGN KEY (`application_id`) REFERENCES `job_application` (`application_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_note_recruiter` FOREIGN KEY (`job_recruiter_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_note_seeker` FOREIGN KEY (`job_seeker_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `job_seeker_note`
--

LOCK TABLES `job_seeker_note` WRITE;
/*!40000 ALTER TABLE `job_seeker_note` DISABLE KEYS */;
INSERT INTO `job_seeker_note` VALUES (1,11,3,1,'Initial screening note.','2025-10-29 22:51:15'),(2,12,3,2,'Resume looks good.','2025-10-29 22:51:15'),(3,13,4,3,'Candidate has .NET experience.','2025-10-29 22:51:15'),(4,14,4,4,'Front-end portfolio reviewed.','2025-10-29 22:51:15'),(5,15,5,5,'Customer service background noted.','2025-10-29 22:51:15'),(6,16,5,6,'Strong cloud fundamentals.','2025-10-29 22:51:15'),(7,17,6,7,'Creative portfolio attached.','2025-10-29 22:51:15'),(8,18,6,8,'Great visual style.','2025-10-29 22:51:15'),(9,19,7,9,'Data analysis skills acceptable.','2025-10-29 22:51:15'),(10,20,7,10,'ML coursework included.','2025-10-29 22:51:15'),(11,11,8,11,'Strong communication skills.','2025-10-29 22:51:15'),(12,12,8,12,'Leadership experience present.','2025-10-29 22:51:15'),(13,13,9,13,'Good DevOps exposure.','2025-10-29 22:51:15'),(14,14,9,14,'SRE interest noted.','2025-10-29 22:51:15'),(15,15,10,15,'HR system understanding strong.','2025-10-29 22:51:15'),(16,16,10,16,'Good familiarity with reporting tools.','2025-10-29 22:51:15'),(17,17,3,17,'Contract role interest confirmed.','2025-10-29 22:51:15'),(18,18,4,18,'API integration knowledge adequate.','2025-10-29 22:51:15'),(19,19,5,19,'Cybersecurity basics covered.','2025-10-29 22:51:15'),(20,20,6,20,'Experience in UI development.','2025-10-29 22:51:15');
/*!40000 ALTER TABLE `job_seeker_note` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `message`
--

DROP TABLE IF EXISTS `message`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `message` (
  `message_id` int NOT NULL AUTO_INCREMENT,
  `conversation_id` int NOT NULL,
  `sender_id` int NOT NULL,
  `receiver_id` int NOT NULL,
  `msg_content` text COLLATE utf8mb4_unicode_ci NOT NULL,
  `msg_timestamp` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `is_read` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`message_id`),
  KEY `ix_message_conv` (`conversation_id`),
  KEY `ix_message_receiver` (`receiver_id`),
  KEY `idx_msg_conv_ts` (`conversation_id`,`msg_timestamp`),
  KEY `idx_msg_recv_unread_ts` (`receiver_id`,`is_read`,`msg_timestamp`),
  KEY `idx_msg_sender_ts` (`sender_id`,`msg_timestamp`),
  CONSTRAINT `fk_message_conv` FOREIGN KEY (`conversation_id`) REFERENCES `conversation` (`conversation_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_message_receiver` FOREIGN KEY (`receiver_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `fk_message_sender` FOREIGN KEY (`sender_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=41 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `message`
--

LOCK TABLES `message` WRITE;
/*!40000 ALTER TABLE `message` DISABLE KEYS */;
INSERT INTO `message` VALUES (1,1,3,11,'Thank you for applying. Are you available for a short call this week?','2025-10-29 22:51:15',0),(2,1,11,3,'Yes, I am available. Please advise a suitable time.','2025-10-29 22:51:15',1),(3,2,3,12,'Thank you for your application. May we schedule an intro discussion?','2025-10-29 22:51:15',0),(4,2,12,3,'Sure, I can make time this week.','2025-10-29 22:51:15',0),(5,3,4,13,'We have received your application. Are you free for a brief call tomorrow?','2025-10-29 22:51:15',0),(6,3,13,4,'Yes, tomorrow works for me.','2025-10-29 22:51:15',0),(7,4,4,14,'Thank you for applying. Could we arrange a preliminary interview?','2025-10-29 22:51:15',0),(8,4,14,4,'Yes, I am available. Please share the details.','2025-10-29 22:51:15',0),(9,5,5,15,'We appreciate your application. Are you open for a short discussion this week?','2025-10-29 22:51:15',0),(10,5,15,5,'Yes, I am open. Kindly propose timing.','2025-10-29 22:51:15',1),(11,6,5,16,'Thank you for your interest. May we schedule an online interview session?','2025-10-29 22:51:15',0),(12,6,16,5,'Yes, an online session is fine for me.','2025-10-29 22:51:15',0),(13,7,6,17,'Thank you for applying. Are you available for an interview this week?','2025-10-29 22:51:15',0),(14,7,17,6,'Yes, I can make time. Please let me know the schedule.','2025-10-29 22:51:15',0),(15,8,6,18,'We have reviewed your application. When are you free for a discussion?','2025-10-29 22:51:15',0),(16,8,18,6,'I am available most afternoons this week.','2025-10-29 22:51:15',0),(17,9,7,19,'Thank you for applying. May we arrange a short intro call?','2025-10-29 22:51:15',0),(18,9,19,7,'Yes, that works. Please share a proposed time.','2025-10-29 22:51:15',0),(19,10,7,20,'Thank you for your application. Are you available for a call soon?','2025-10-29 22:51:15',0),(20,10,20,7,'Yes, I am available. Please advise date and time.','2025-10-29 22:51:15',0),(21,11,8,11,'We have received your application. Can we schedule a quick discussion?','2025-10-29 22:51:15',0),(22,11,11,8,'Yes, I can make time. Let me know your availability.','2025-10-29 22:51:15',0),(23,12,8,12,'Thank you for applying. Are you open for a call this week?','2025-10-29 22:51:15',0),(24,12,12,8,'Yes, I am available this week.','2025-10-29 22:51:15',0),(25,13,9,13,'Thank you for your interest. When can we arrange a short call?','2025-10-29 22:51:15',0),(26,13,13,9,'I am available most mornings.','2025-10-29 22:51:15',0),(27,14,9,14,'We would like to discuss your application. Are you available soon?','2025-10-29 22:51:15',0),(28,14,14,9,'Yes, please propose a time.','2025-10-29 22:51:15',0),(29,15,10,15,'Thank you for applying. May we set an interview schedule?','2025-10-29 22:51:15',0),(30,15,15,10,'Yes, I am available. Please advise timing.','2025-10-29 22:51:15',0),(31,16,10,16,'We have reviewed your profile. Can we arrange a call?','2025-10-29 22:51:15',0),(32,16,16,10,'Yes, that works. Let me know the schedule.','2025-10-29 22:51:15',0),(33,17,3,17,'Thank you for applying. Are you available for a call this week?','2025-10-29 22:51:15',0),(34,17,17,3,'Yes, I am available this week.','2025-10-29 22:51:15',1),(35,18,4,18,'We appreciate your application. May we schedule an interview?','2025-10-29 22:51:15',0),(36,18,18,4,'Yes, please share the details.','2025-10-29 22:51:15',0),(37,19,5,19,'Thank you for your interest. When are you free for a call?','2025-10-29 22:51:15',0),(38,19,19,5,'I am free most weekdays.','2025-10-29 22:51:15',0),(39,20,6,20,'We would like to discuss your application soon. Are you available?','2025-10-29 22:51:15',0),(40,20,20,6,'Yes, I am available. Please advise the schedule.','2025-10-29 22:51:15',0);
/*!40000 ALTER TABLE `message` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `notification`
--

DROP TABLE IF EXISTS `notification`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `notification` (
  `notification_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `notification_title` varchar(160) COLLATE utf8mb4_unicode_ci NOT NULL,
  `notification_msg` text COLLATE utf8mb4_unicode_ci NOT NULL,
  `notification_type` varchar(50) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `notification_date_created` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `notification_read_status` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`notification_id`),
  KEY `ix_notification_user` (`user_id`),
  CONSTRAINT `fk_notification_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `notification`
--

LOCK TABLES `notification` WRITE;
/*!40000 ALTER TABLE `notification` DISABLE KEYS */;
INSERT INTO `notification` VALUES (1,11,'Application Update','Your application has been received.','Application','2025-10-29 22:51:15',0),(2,12,'Application Update','Your application has been received.','Application','2025-10-29 22:51:15',0),(3,13,'Application Update','Your application has been received.','Application','2025-10-29 22:51:15',0),(4,14,'Application Update','Your application has been received.','Application','2025-10-29 22:51:15',0),(5,15,'Application Update','Your application has been received.','Application','2025-10-29 22:51:15',0),(6,16,'Screening Scheduled','A recruiter has requested a call.','Interview','2025-10-29 22:51:15',0),(7,17,'Screening Scheduled','A recruiter has requested a call.','Interview','2025-10-29 22:51:15',0),(8,18,'Screening Scheduled','A recruiter has requested a call.','Interview','2025-10-29 22:51:15',0),(9,19,'Screening Scheduled','A recruiter has requested a call.','Interview','2025-10-29 22:51:15',0),(10,20,'Screening Scheduled','A recruiter has requested a call.','Interview','2025-10-29 22:51:15',0),(11,3,'New Application','A new applicant has applied to your job posting.','Employer','2025-10-29 22:51:15',0),(12,4,'New Application','A new applicant has applied to your job posting.','Employer','2025-10-29 22:51:15',0),(13,5,'New Application','A new applicant has applied to your job posting.','Employer','2025-10-29 22:51:15',0),(14,6,'New Application','A new applicant has applied to your job posting.','Employer','2025-10-29 22:51:15',0),(15,7,'New Application','A new applicant has applied to your job posting.','Employer','2025-10-29 22:51:15',0),(16,8,'Profile Viewed','A job seeker viewed your company profile.','Employer','2025-10-29 22:51:15',0),(17,9,'Profile Viewed','A job seeker viewed your company profile.','Employer','2025-10-29 22:51:15',0),(18,10,'Profile Viewed','A job seeker viewed your company profile.','Employer','2025-10-29 22:51:15',0),(19,1,'System Action','Automatic review logs have been updated.','System','2025-10-29 22:51:15',0),(20,2,'System Action','Automatic review logs have been updated.','System','2025-10-29 22:51:15',0);
/*!40000 ALTER TABLE `notification` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `notification_preference`
--

DROP TABLE IF EXISTS `notification_preference`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `notification_preference` (
  `preference_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `allow_email` tinyint(1) NOT NULL DEFAULT '1',
  `allow_inApp` tinyint(1) NOT NULL DEFAULT '1',
  `allow_SMS` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`preference_id`),
  KEY `fk_pref_user` (`user_id`),
  CONSTRAINT `fk_pref_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `notification_preference`
--

LOCK TABLES `notification_preference` WRITE;
/*!40000 ALTER TABLE `notification_preference` DISABLE KEYS */;
INSERT INTO `notification_preference` VALUES (1,1,1,1,0),(2,2,1,1,0),(3,3,1,1,0),(4,4,1,1,0),(5,5,1,1,0),(6,6,1,1,0),(7,7,1,1,0),(8,8,1,1,0),(9,9,1,1,0),(10,10,1,1,0),(11,11,1,1,0),(12,12,1,1,0),(13,13,1,1,0),(14,14,1,1,0),(15,15,1,1,0),(16,16,1,1,0),(17,17,1,1,0),(18,18,1,1,0),(19,19,1,1,0),(20,20,1,1,0);
/*!40000 ALTER TABLE `notification_preference` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `resume`
--

DROP TABLE IF EXISTS `resume`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `resume` (
  `resume_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `upload_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `file_path` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`resume_id`),
  KEY `ix_resume_user` (`user_id`),
  CONSTRAINT `fk_resume_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `resume`
--

LOCK TABLES `resume` WRITE;
/*!40000 ALTER TABLE `resume` DISABLE KEYS */;
INSERT INTO `resume` VALUES (1,11,'2025-10-29 22:51:15','/uploads/hafiz_resume_v1.pdf'),(2,11,'2025-10-29 22:51:15','/uploads/resumes/Lee_Zi_Hern_Resume.pdf'),(3,12,'2025-10-29 22:51:15','/uploads/aisyah_resume_v1.pdf'),(4,12,'2025-10-29 22:51:15','/uploads/aisyah_resume_v2.pdf'),(5,13,'2025-10-29 22:51:15','/uploads/amira_resume_v1.pdf'),(6,13,'2025-10-29 22:51:15','/uploads/amira_resume_v2.pdf'),(7,14,'2025-10-29 22:51:15','/uploads/jason_resume_v1.pdf'),(8,14,'2025-10-29 22:51:15','/uploads/jason_resume_v2.pdf'),(9,15,'2025-10-29 22:51:15','/uploads/syafiqah_resume_v1.pdf'),(10,15,'2025-10-29 22:51:15','/uploads/syafiqah_resume_v2.pdf'),(11,16,'2025-10-29 22:51:15','/uploads/irfan_resume_v1.pdf'),(12,16,'2025-10-29 22:51:15','/uploads/irfan_resume_v2.pdf'),(13,17,'2025-10-29 22:51:15','/uploads/suresh_resume_v1.pdf'),(14,17,'2025-10-29 22:51:15','/uploads/suresh_resume_v2.pdf'),(15,18,'2025-10-29 22:51:15','/uploads/meiling_resume_v1.pdf'),(16,18,'2025-10-29 22:51:15','/uploads/meiling_resume_v2.pdf'),(17,19,'2025-10-29 22:51:15','/uploads/zulkifli_resume_v1.pdf'),(18,19,'2025-10-29 22:51:15','/uploads/zulkifli_resume_v2.pdf'),(19,20,'2025-10-29 22:51:15','/uploads/farhana_resume_v1.pdf'),(20,20,'2025-10-29 22:51:15','/uploads/farhana_resume_v2.pdf');
/*!40000 ALTER TABLE `resume` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `template`
--

DROP TABLE IF EXISTS `template`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `template` (
  `template_id` int NOT NULL AUTO_INCREMENT,
  `user_id` int NOT NULL,
  `template_name` varchar(120) COLLATE utf8mb4_unicode_ci NOT NULL,
  `template_subject` varchar(160) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `template_body` text COLLATE utf8mb4_unicode_ci NOT NULL,
  `template_status` enum('Active','Archived') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Active',
  `date_created` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `date_updated` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`template_id`),
  KEY `fk_template_user` (`user_id`),
  CONSTRAINT `fk_template_user` FOREIGN KEY (`user_id`) REFERENCES `user` (`user_id`)
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `template`
--

LOCK TABLES `template` WRITE;
/*!40000 ALTER TABLE `template` DISABLE KEYS */;
INSERT INTO `template` VALUES (1,3,'Interview Invite','Interview for {{JobTitle}}','Hi {{FirstName}},\n\nThanks for applying for {{JobTitle}} at {{Company}}. We\'d like to speak with you briefly. Are you available on {{Date}} at {{Time}}?\n\nBest regards,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(2,3,'Rejection - Not a Fit','Application for {{JobTitle}}','Hi {{FirstName}},\n\nThank you for your interest in {{JobTitle}}. After review, we will not be moving forward at this time.\n\nWe appreciate the effort you put into applying.\n{{Company}} Recruitment','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(3,3,'Next Steps','Next steps for {{JobTitle}}','Hi {{FirstName}},\n\nWe\'re happy to proceed to the next stage. Please complete:\n- Task: {{TaskName}}\n- Due: {{DueDate}}\n\nThank you,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(4,4,'Call Availability Request','Checking Availability for {{JobTitle}}','Hi {{FirstName}},\n\nHope you\'re doing well. When would you be available for a short call regarding {{JobTitle}}?\n\nBest,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(5,4,'Portfolio Request','Portfolio Request for {{JobTitle}}','Hi {{FirstName}},\n\nCould you please share your portfolio or recent work samples? This will help us evaluate your fit for {{JobTitle}}.\n\nThanks,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(6,5,'Interview Confirmation','Interview Confirmed for {{JobTitle}}','Hi {{FirstName}},\n\nYour interview for {{JobTitle}} has been confirmed for {{Date}} at {{Time}}.\nLooking forward to speaking with you.\n\nRegards,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(7,5,'Follow-Up Reminder','Follow-Up for {{JobTitle}}','Hi {{FirstName}},\n\nJust checking in to see if you\'re still interested in {{JobTitle}}.\n\nLet me know anytime,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(8,6,'Technical Assessment','Assessment for {{JobTitle}}','Hi {{FirstName}},\n\nThanks for your interest in {{JobTitle}}. Please complete the technical assessment using the link below:\n{{AssessmentLink}}\n\nGood luck!\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(9,6,'Soft Skills Interview','Next Step for {{JobTitle}}','Hi {{FirstName}},\n\nWe\'d like to schedule a soft skills interview.\nPlease share your availability.\n\nThanks,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(10,7,'Offer Discussion','Discussing Your Offer','Hi {{FirstName}},\n\nWe\'d like to discuss the potential offer for {{JobTitle}}.\nAre you free for a call sometime this week?\n\nRegards,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(11,7,'Document Request','Documents Required for {{JobTitle}}','Hi {{FirstName}},\n\nCould you provide copies of:\n- IC / Passport\n- Latest Payslip (if applicable)\n\nThank you,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(12,8,'Interview Reschedule','Reschedule Interview for {{JobTitle}}','Hi {{FirstName}},\n\nWe may need to adjust the interview time.\nLet me know your flexibility.\n\nThanks,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(13,8,'Job Application Update','Update on Your Application for {{JobTitle}}','Hi {{FirstName}},\n\nYour application is still under review. We will update you soon.\n\nWarm regards,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(14,9,'Final Interview Invite','Final Round for {{JobTitle}}','Hi {{FirstName}},\n\nCongratulations — we’d like to invite you to a final discussion for {{JobTitle}}.\nPlease confirm your availability.\n\nBest,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(15,9,'Reference Check','Reference Check for {{JobTitle}}','Hi {{FirstName}},\n\nWe are now conducting reference checks.\nPlease provide contact details of 1–2 past supervisors.\n\nThanks,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(16,10,'Welcome Message','Welcome to {{Company}}!','Hi {{FirstName}},\n\nWelcome aboard! We’re excited to have you as part of {{Company}}.\nWe will send onboarding steps shortly.\n\nWarm regards,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(17,10,'Onboarding Instructions','Onboarding for {{JobTitle}}','Hi {{FirstName}},\n\nHere are your onboarding steps:\n1. Complete HR forms\n2. Review company handbook\n3. Confirm equipment needs\n\nWelcome!\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(18,3,'Interview Reminder','Reminder: Interview for {{JobTitle}}','Hi {{FirstName}},\n\nThis is a friendly reminder for your interview scheduled on {{Date}} at {{Time}}.\n\nSee you soon,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(19,4,'Availability Check','Checking Schedule for {{JobTitle}}','Hi {{FirstName}},\n\nCould you share your availability for a follow-up conversation?\n\nRegards,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15'),(20,6,'General Follow-Up','Following Up on {{JobTitle}}','Hi {{FirstName}},\n\nJust touching base — let me know if you have any updates on your end.\n\nBest wishes,\n{{RecruiterName}}','Active','2025-10-29 22:51:15','2025-10-29 22:51:15');
/*!40000 ALTER TABLE `template` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `user`
--

DROP TABLE IF EXISTS `user`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `user` (
  `user_id` int NOT NULL AUTO_INCREMENT,
  `first_name` varchar(60) COLLATE utf8mb4_unicode_ci NOT NULL,
  `last_name` varchar(60) COLLATE utf8mb4_unicode_ci NOT NULL,
  `email` varchar(190) COLLATE utf8mb4_unicode_ci NOT NULL,
  `password_hash` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL,
  `user_role` enum('Admin','Recruiter','JobSeeker') COLLATE utf8mb4_unicode_ci NOT NULL,
  `user_2FA` tinyint(1) NOT NULL DEFAULT '0',
  `user_2FA_secret` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `user_status` enum('Active','Suspended') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Active',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`),
  UNIQUE KEY `email` (`email`)
) ENGINE=InnoDB AUTO_INCREMENT=21 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `user`
--

LOCK TABLES `user` WRITE;
/*!40000 ALTER TABLE `user` DISABLE KEYS */;
INSERT INTO `user` VALUES (1,'Mohamad','Iskandar','mohamad.iskandar@example.com','HASH','Admin',0,NULL,'Active','2025-10-29 22:51:15'),(2,'Aminah','Zul','aminah.zul@example.com','HASH','Admin',0,NULL,'Active','2025-10-29 22:51:15'),(3,'Farid','Hassan','farid.hassan@talentace.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(4,'Siti','Aminah','siti.aminah@codewave.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(5,'Rashid','BinAli','rashid.ali@techforge.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(6,'Nurul','Hidayah','nurul.hidayah@pixelhub.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(7,'Wong','Liang','wong.liang@novalabs.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(8,'Kumar','Ravichandran','kumar.r@recruitnow.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(9,'Lee','Chong','lee.chong@rocketforge.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(10,'Haslina','Kamaruddin','haslina.k@hrsmart.com','HASH','Recruiter',0,NULL,'Active','2025-10-29 22:51:15'),(11,'Hafiz','Rahman','hafiz.rahman@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(12,'Aisyah','BintiSalim','aisyah.salim@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(13,'Amira','Lee','amira.lee@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(14,'Jason','Tan','jason.tan@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(15,'Nur','Syafiqah','nur.syafiqah@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(16,'Irfan','Kamil','irfan.kamil@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(17,'Suresh','Nair','suresh.nair@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(18,'Mei','Ling','mei.ling@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(19,'Zulkifli','Abdullah','zulkifli.abdullah@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15'),(20,'Farhana','Aziz','farhana.aziz@example.com','HASH','JobSeeker',0,NULL,'Active','2025-10-29 22:51:15');
/*!40000 ALTER TABLE `user` ENABLE KEYS */;
UNLOCK TABLES;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2025-10-30 14:55:15
