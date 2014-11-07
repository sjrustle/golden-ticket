﻿using System;
using System.Data.Entity;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using GoldenTicket.Models;
using GoldenTicket.DAL;

namespace GoldenTicket.Controllers
{
    public class RegistrationController : Controller
    {
        private readonly GoldenTicketDbContext database = new GoldenTicketDbContext();
        private GlobalConfig config;

        private static readonly DateTime AGE_4_BY_DATE = new DateTime(DateTime.Today.Year, 9, 1);

        public RegistrationController()
        {
            config = database.GlobalConfigs.First();
        }

        // GET: Registration
        public ActionResult Index()
        {
            Session.Clear();

            return View();
        }

        public ActionResult StudentInformation()
        {
            StudentInformationViewSetup();

            var applicant = GetSessionApplicant();

            return View(applicant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult StudentInformation(Applicant applicant)
        {
            // Make sure someone isn't playing with the ID from the form
            if(!IsAuthorizedApplicant(applicant) || !IsActiveSession()) // TODO Use AOP/Annotations to do this instead
            {
                return RedirectToAction("Index");
            }

            // Check for required fields
            if(string.IsNullOrEmpty(applicant.StudentFirstName))
            {
                ModelState.AddModelError("StudentFirstName", "Student first name must be entered");
            }
            if (string.IsNullOrEmpty(applicant.StudentLastName))
            {
                ModelState.AddModelError("StudentLastName", "Student last name must be entered");
            }
            if (string.IsNullOrEmpty(applicant.StudentStreetAddress1))
            {
                ModelState.AddModelError("StudentStreetAddress1", "Student street address (line 1) must be entered");
            }
            if (string.IsNullOrEmpty(applicant.StudentCity))
            {
                ModelState.AddModelError("StudentCity", "Student city must be entered");
            }
            if (string.IsNullOrEmpty(applicant.StudentZipCode))
            {
                ModelState.AddModelError("StudentZipCode", "Student ZIP code must be entered");
            }
            if (applicant.StudentBirthday == null)
            {
                ModelState.AddModelError("StudentBirthday", "Student birthday must be entered");
            }
            if (applicant.StudentGender == null)
            {
                ModelState.AddModelError("StudentGender", "Student gender must be entered");
            }

            if (applicant.StudentBirthday != null && !IsAgeEligible(applicant.StudentBirthday.Value))
            {
                ModelState.AddModelError("StudentBirthday", "Student is not old enough for pre-kindergarten. Try again next year or fix the birthday if it was entered wrong.");
            }

            // Valid fields
            if(ModelState.IsValid)
            {
                SaveStudentInformation(applicant);

                return RedirectToAction("GuardianInformation");
            }

            // Invalid fields
            StudentInformationViewSetup();
            return View(applicant);
        }

        public ActionResult GuardianInformation()
        {
            if (!IsActiveSession()) //TODO Do this with AOP/Annotations instead
            {
                return RedirectToAction("Index");
            }

            GuardianInformationViewSetup();

            var applicant = GetSessionApplicant();

            return View(applicant); 
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardianInformation(Applicant applicant)
        {
            // Make sure someone isn't playing with the ID from the form
            if (!IsAuthorizedApplicant(applicant) || !IsActiveSession()) // TODO Use AOP/Annotations to do this instead
            {
                return RedirectToAction("Index");
            }

            // Check required fields
            if( string.IsNullOrEmpty(applicant.Contact1FirstName) )
            {
                ModelState.AddModelError("Contact1FirstName", "Guardian first name must be entered");
            }
            if( string.IsNullOrEmpty(applicant.Contact1LastName) )
            {
                ModelState.AddModelError("Contact1FirstName", "Guardian last name must be entered");
            }
            if( string.IsNullOrEmpty(applicant.Contact1Phone) )
            {
                ModelState.AddModelError("Contact1Phone", "Guardian phone number must be entered");
            }
            if( string.IsNullOrEmpty(applicant.Contact1Email) )
            {
                ModelState.AddModelError("Contact1Phone", "Guardian email address must be entered");
            }
            if( string.IsNullOrEmpty(applicant.Contact1Relationship) )
            {
                ModelState.AddModelError("Contact1Relationship", "Guardian relationship must be entered");
            }          
            if( applicant.HouseholdMembers == null )
            {
                ModelState.AddModelError("HouseholdMembers", "The number of household members must be entered");
            }
            if( applicant.HouseholdMonthlyIncome == null)
            {
                ModelState.AddModelError("HouseholdMonthlyIncome", "The average monthly income must be entered or selected");
            }

            // Validate model
            if(ModelState.IsValid)
            {
                SaveGuardianInformation(applicant);

                return RedirectToAction("SchoolSelection");
            }

            // Invalid model
            GuardianInformationViewSetup();
            return View(applicant);
        }

        public ActionResult SchoolSelection()
        {
            if (!IsActiveSession()) //TODO Do this with AOP/Annotations instead
            {
                return RedirectToAction("Index");
            }

            var applicant = GetSessionApplicant();
            SchoolInformationViewSetup(applicant);

            return View(applicant); 
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SchoolSelection(Applicant applicant, FormCollection formCollection)
        {
            // Make sure someone isn't playing with the ID from the form
            if (!IsAuthorizedApplicant(applicant) || !IsActiveSession()) // TODO Use AOP/Annotations to do this instead
            {
                return RedirectToAction("Index");
            }

            // At least one program needs to be selected
            var programIds = new List<int>();
            if(formCollection["programs"] == null || formCollection["programs"].Count() <= 0)
            {
                ModelState.AddModelError("programs", "At least one program must be chosen");
                SchoolInformationViewSetup(applicant);
                return View(applicant);
            }
            else
            {
                var programIdStrs = formCollection["programs"].Split(',').ToList();
                programIdStrs.ForEach(idStr => programIds.Add(int.Parse(idStr)));
            }

            // Remove existing applications for this user
            var applieds = database.Applieds.Where(applied => applied.ApplicantID == applicant.ID).ToList();
            applieds.ForEach(a => database.Applieds.Remove(a));

            // Add new Applied associations (between program and program)
            var populatedApplicant = database.Applicants.Find(applicant.ID);
            foreach( var programId in programIds )
            {
                var applied = new Applied();
                applied.ApplicantID = applicant.ID;
                applied.ProgramID = programId;

                // Confirm that the program ID is within the city lived in (no sneakers into other districts)
                var program = database.Programs.Find(programId);
                if(program != null && program.City.Equals(populatedApplicant.StudentCity, StringComparison.CurrentCultureIgnoreCase))
                {
                    database.Applieds.Add(applied);
                }
            }

            database.SaveChanges();
            return RedirectToAction("Review");
        }

        public ActionResult Review()
        {
            if(!IsActiveSession()) //TODO Do this with AOP/Annotations instead
            {
                return RedirectToAction("Index");
            }

            var applicant = GetSessionApplicant();
            
            ReviewViewSetup(applicant);

            return View(applicant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Review(Applicant applicant)
        {
            // Make sure someone isn't playing with the ID from the form
            if (!IsAuthorizedApplicant(applicant) || !IsActiveSession()) // TODO Use AOP/Annotations to do this instead
            {
                return RedirectToAction("Index");
            }

            applicant.ConfirmationCode = Guid.NewGuid().ToString();
            SaveReview(applicant);

            return RedirectToAction("Confirmation");
        }

        public ActionResult Confirmation()
        {
            if (!IsActiveSession()) //TODO Do this with AOP/Annotations instead
            {
                return RedirectToAction("Index");
            }

            var applicant = GetSessionApplicant();

            Session.Clear();

            return View(applicant);
        }

        // ---- Helper Fields ----
        private void StudentInformationViewSetup()
        {
            ViewBag.DistrictNames = GetDistrictNames();   
        }

        private void GuardianInformationViewSetup()
        {
            ViewBag.IncomeRanges = GetIncomeRanges();
        }

        private string[] GetDistrictNames()
        {
            ISet<string> districtNames = new HashSet<string>();
            districtNames.Add("");

            foreach(Program program in database.Programs)
            {
                districtNames.Add(program.City);
            }

            return districtNames.OrderBy(s=>s).ToArray();
        }

        private IEnumerable<SelectListItem> GetIncomeRanges()
        {
            var incomeRanges = new List<SelectListItem>();

            int previousIncomeLine = 0;
            foreach( int householdMembers in Enumerable.Range(2,9))
            {
                var povertyConfig = database.PovertyConfigs.First(p => p.HouseholdMembers == householdMembers);
                var item = new SelectListItem
                {
                    Text = previousIncomeLine.ToString("C") + " to " + povertyConfig.MinimumIncome.ToString("C"),
                    Value = povertyConfig.MinimumIncome.ToString()
                };

                previousIncomeLine = povertyConfig.MinimumIncome;
                incomeRanges.Add(item);
            }

            return incomeRanges;
        }

        private void SaveStudentInformation(Applicant applicant)
        {
            // Add a new applicant
            if(applicant.ID == 0)
            {
                database.Applicants.Add(applicant);
            }
            // Modify an existing applicant
            else
            {
                database.Applicants.Attach(applicant);
                var applicantEntry = database.Entry(applicant);

                applicantEntry.Property(a => a.StudentFirstName).IsModified = true;
                applicantEntry.Property(a => a.StudentMiddleName).IsModified = true;
                applicantEntry.Property(a => a.StudentLastName).IsModified = true;
                applicantEntry.Property(a => a.StudentStreetAddress1).IsModified = true;
                applicantEntry.Property(a => a.StudentStreetAddress2).IsModified = true;
                applicantEntry.Property(a => a.StudentCity).IsModified = true;
                applicantEntry.Property(a => a.StudentZipCode).IsModified = true;
                applicantEntry.Property(a => a.StudentBirthday).IsModified = true;
                applicantEntry.Property(a => a.StudentGender).IsModified = true;
            }

            database.SaveChanges();
            Session["applicantID"] = applicant.ID;
        }

        private Applicant GetSessionApplicant()
        {
            Applicant applicant = null;
            if (Session["applicantID"] != null)
            {
                applicant = database.Applicants.Find((int) Session["applicantID"]);
            }
            else
            {
                applicant = new Applicant();
                SaveStudentInformation(applicant);
            }

            return applicant;
        }

        private bool IsAuthorizedApplicant(Applicant applicant)
        {
            // Make sure that the student is the one the user is authorized to make (i.e. if an ID is given, it should be the same one in the session)
            bool isApplicantNew = applicant.ID == 0;
            bool isActiveSession = Session["applicantID"] != null;

            // If a new sessions
            if (isApplicantNew && !isActiveSession)
            {
                return true;
            }

            // If existing session, check to make sure session applicant ID matches the one submitted
            bool isActiveApplicantSameAsSubmitted = applicant.ID.Equals(Session["applicantID"]);
            return !isApplicantNew && isActiveSession && isActiveApplicantSameAsSubmitted;
        }

        private static bool IsAgeEligible(DateTime birthday)
        {
            int ageByCutoff = AGE_4_BY_DATE.Year - birthday.Year;
            DateTime adjustedDate = AGE_4_BY_DATE.AddYears(-ageByCutoff);
            if(birthday > adjustedDate)
            {
                ageByCutoff--;
            }

            return (ageByCutoff == 4);
        }

        private void SaveGuardianInformation(Applicant applicant)
        {
            database.Applicants.Attach(applicant);
            var applicantEntry = database.Entry(applicant);

            applicantEntry.Property(a => a.Contact1FirstName).IsModified = true;
            applicantEntry.Property(a => a.Contact1LastName).IsModified = true;
            applicantEntry.Property(a => a.Contact1Phone).IsModified = true;
            applicantEntry.Property(a => a.Contact1Email).IsModified = true;
            applicantEntry.Property(a => a.Contact1Relationship).IsModified = true;
            applicantEntry.Property(a => a.Contact2FirstName).IsModified = true;
            applicantEntry.Property(a => a.Contact2LastName).IsModified = true;
            applicantEntry.Property(a => a.Contact2Phone).IsModified = true;
            applicantEntry.Property(a => a.Contact2Email).IsModified = true;
            applicantEntry.Property(a => a.Contact2Relationship).IsModified = true;
            applicantEntry.Property(a => a.HouseholdMembers).IsModified = true;
            applicantEntry.Property(a => a.HouseholdMonthlyIncome).IsModified = true;

            database.SaveChanges();
        }

        private void SchoolInformationViewSetup(Applicant applicant)
        {
            var eligiblePrograms = database.Programs.Where(p => p.City == applicant.StudentCity).OrderBy(p => p.Name).ToList();
            ViewBag.Programs = eligiblePrograms;

            var applieds = database.Applieds.Where(a => a.ApplicantID == applicant.ID).ToList();
            ViewBag.Applieds = applieds;
        }

        private void ReviewViewSetup(Applicant applicant)
        {
            var applieds = database.Applieds.Where(a => a.ApplicantID == applicant.ID).ToList();
            var programs = new List<Program>();

            applieds.ForEach(a => programs.Add(a.Program));

            ViewBag.Programs = programs;

            ViewBag.NotificationDate = config.NotificationDate;
        }

        private void SaveReview(Applicant applicant)
        {
            database.Applicants.Attach(applicant);
            var applicantEntry = database.Entry(applicant);

            applicantEntry.Property(a => a.ConfirmationCode).IsModified = true;

            database.SaveChanges();
        }

        private bool IsActiveSession()
        {
            return Session["applicantID"] != null;
        }
    }
}