﻿using System;
using System.Collections.Generic;
using System.Data.Entity.Migrations;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using GoldenTicket.Misc;
using GoldenTicket.Models;
using GoldenTicket.DAL;
using GoldenTicket.Resources;

namespace GoldenTicket.Controllers
{
    public class AdminController : Controller
    {

        private GoldenTicketDbContext db = new GoldenTicketDbContext();
        private static readonly School ALL_SCHOOL_SCHOOL = GetAllSchoolSchool();

        private readonly SharedViewHelper viewHelper;

        public AdminController()
        {
            viewHelper = new SharedViewHelper(db);
        }


        // All Applications
        // GET: Admin
        public ActionResult Index()
        {
            return RedirectToAction("ViewApplicants");
        }


        private void PrepareApplicationsView()
        {
            ViewBag.LotteryRunDate = GetLotteryRunDate();
            ViewBag.LotteryCloseDate = GetLotteryCloseDate();
            ViewBag.IsLotteryClosed = ViewBag.LotteryCloseDate <= DateTime.Now;

            AddSchoolsToViewBag();
        }

        public ActionResult ViewApplicants(int? id)
        {

            if (id == null || id <= 0)
            {
                id = 0;
            }
            else
            {
                id = id - 1;
            }

            PrepareApplicationsView();
            ViewBag.School = ALL_SCHOOL_SCHOOL;

            // Get the applicants based on the page
            var numApplicants = db.Applicants.Count();
            var skipCount = id.Value*100;

            if (numApplicants < skipCount)
            {
                skipCount = 0;
                id = 0;
            }

            var applicants =
                db.Applicants.Where(a => a.ConfirmationCode != null)
                    .OrderBy(a => a.StudentLastName)
                    .Skip(skipCount)
                    .Take(100)
                    .ToList();
            ViewBag.Applicants = applicants;

            // Pagination
            var numPages = numApplicants/100;
            if (numApplicants%100 >= 1)
            {
                numPages += 1;
            }
            ViewBag.NumPages = numPages;
            ViewBag.PageNum = id + 1;

            return View();
        }

        public ActionResult ExportApplicants()
        {
            var applicants = db.Applicants.OrderBy(a=>a.StudentLastName).ToList();

            return ExportApplicantsCsvFile(applicants, "applicants_all.csv");
        }

        public ActionResult ExportApplicantsForSchool(int id)
        {
            var school = db.Schools.Find(id);
            if (school == null)
            {
                return HttpNotFound();
            }

            var applieds = db.Applieds.Where(a => a.SchoolID == id).OrderBy(a=>a.Applicant.StudentLastName).ToList();
            var applicants = Utils.GetApplicants(applieds);

            var schoolName = db.Schools.Find(id).Name.Replace(' ', '_');
            return ExportApplicantsCsvFile(applicants, "applicants_" + schoolName + ".csv");
        }


        public ActionResult ViewApplicantsForSchool(int? id)
        {
            if (id == null)
            {
                return RedirectToAction("ViewApplicants");
            }


            // If the school ID is not valid, show all the applicants
            ViewBag.School = db.Schools.Find(id);
            if (ViewBag.School == null)
            {
                return RedirectToAction("ViewApplicants", 0);
            }

            // If the lottery was run, get the selected and waitlisted applicants
            ViewBag.WasLotteryRun = WasLotteryRun();
            if (ViewBag.WasLotteryRun)
            {
                var selecteds = db.Selecteds.Where(s => s.SchoolID == id).OrderBy(s => s.Rank).ToList();
                var selectedApplicants = new List<Applicant>();
                foreach (var selected in selecteds) // don't convert to LINQ -- needs to preserve order
                {
                    selectedApplicants.Add(selected.Applicant);
                }
                ViewBag.SelectedApplicants = selectedApplicants;

                var waitlisteds = db.Waitlisteds.Where(w => w.SchoolID == id).OrderBy(w => w.Rank).ToList();
                var waitlistedApplicants = new List<Applicant>();
                foreach (var waitlisted in waitlisteds) // don't convert to LINQ -- needs to preserve order
                {
                   waitlistedApplicants.Add(waitlisted.Applicant);
                }
                ViewBag.WaitlistedApplicants = waitlistedApplicants;
            }
            else
            {
                var applieds = db.Applieds.Where(a => a.SchoolID == id).OrderBy(a => a.Applicant.StudentLastName).ToList();
                var applicants = new List<Applicant>();
                foreach (var applied in applieds) // don't convert to LINQ -- needs to preserve order
                {
                    applicants.Add(applied.Applicant);
                }
                ViewBag.Applicants = applicants;
            }

            // Other things needed for display
            AddSchoolsToViewBag();

            return View();
        }
        
        private void PrepareApplicantDetailView(Applicant applicant)
        {
            ViewBag.AppliedSchools =
                Utils.GetSchools(db.Applieds.Where(a => a.ApplicantID == applicant.ID).OrderBy(a => a.School.Name).ToList());
            var selectedSchool = db.Selecteds.FirstOrDefault(s => s.ApplicantID == applicant.ID);
            if (selectedSchool != null)
            {
                ViewBag.SelectedSchool = selectedSchool;
            }
            ViewBag.WaitlistedSchools =
                Utils.GetSchools(db.Waitlisteds.Where(a => a.ApplicantID == applicant.ID).OrderBy(a => a.School.Name).ToList());
            ViewBag.WasLotteryRun = GetLotteryRunDate() != null;
        }

        public ActionResult ViewApplicant(int id)
        {
            var applicant = db.Applicants.Find(id);

            // Send back to view all applicants if an incorrect ID is specified
            if(applicant == null)
            {
                return RedirectToAction("ViewApplicants");
            }

            // Variables for display
            PrepareApplicantDetailView(applicant);

            return View(applicant);
        }

        public ActionResult EditApplicant(int id)
        {
            var applicant = db.Applicants.Find(id);
            if (applicant == null)
            {
                return HttpNotFound();
            }

            PrepareEditApplicantView(applicant);

            return View(applicant);
        }

        [HttpPost]
        public ActionResult EditApplicant(Applicant applicant, FormCollection formCollection)
        {
            var queriedApplicant = db.Applicants.Find(applicant.ID);
            if (queriedApplicant == null)
            {
                return HttpNotFound();
            }

            // Empty check student and guardian information
            viewHelper.EmptyCheckStudentInformation(ModelState, applicant);
            viewHelper.EmptyCheckGuardianInformation(ModelState, applicant);

            // School selection check //TODO Make this code shareable with the parent side
            var schoolIds = new List<int>();
            if (formCollection["programs"] == null || !formCollection["programs"].Any())
            {
                ModelState.AddModelError("programs", GoldenTicketText.NoSchoolSelected);
                PrepareEditApplicantView(applicant);
                return View(applicant);
            }
            else
            {
                var programIdStrs = formCollection["programs"].Split(',').ToList();
                programIdStrs.ForEach(idStr => schoolIds.Add(int.Parse(idStr)));
            }

            if (!ModelState.IsValid)
            {
                PrepareEditApplicantView(applicant);
                return View(applicant);
            }

            // Remove existing applications for this user
            var applieds = db.Applieds.Where(applied => applied.ApplicantID == applicant.ID).ToList();
            applieds.ForEach(a => db.Applieds.Remove(a));

            // Add new Applied associations (between program and program)
            var populatedApplicant = db.Applicants.Find(applicant.ID);
            foreach (var programId in schoolIds)
            {
                var applied = new Applied();
                applied.ApplicantID = applicant.ID;
                applied.SchoolID = programId;

                // Confirm that the program ID is within the city lived in (no sneakers into other districts)
                var program = db.Schools.Find(programId);
                if (program != null && program.City.Equals(populatedApplicant.StudentCity, StringComparison.CurrentCultureIgnoreCase))
                {
                    db.Applieds.Add(applied);
                }
            }

            db.Applicants.AddOrUpdate(applicant);
            db.SaveChanges();

            return RedirectToAction("ViewApplicant", new{id=applicant.ID});
        }


        public ActionResult ViewDuplicateApplicants()
        {
            var schoolDuplicates = new Dictionary<School, List<Applicant>>();
            
            var schools = db.Schools.OrderBy(s => s.Name).ToList();
            foreach (var s in schools)
            {
                var applieds =
                    db.Applieds.Where(a => a.SchoolID == s.ID)
                        .OrderBy(a => a.Applicant.StudentLastName)
                        .ThenBy(a => a.Applicant.StudentFirstName)
                        .ToList();
                var duplicates = Utils.GetDuplicateApplicants(Utils.GetApplicants(applieds));

                schoolDuplicates.Add(s,duplicates);
            }

            return View(schoolDuplicates);
        }

        public ActionResult DeleteApplicant(int id)
        {
            var applicant = db.Applicants.Find(id);
            if (applicant == null)
            {
                return HttpNotFound();
            }

            PrepareApplicantDetailView(applicant);

            return View(applicant);
        }

        [HttpPost]
        public ActionResult DeleteApplicant(Applicant applicant)
        {
            var queriedApplicant = db.Applicants.Find(applicant.ID);
            if (queriedApplicant == null)
            {
                return HttpNotFound();
            }

            db.Applicants.Remove(queriedApplicant);
            db.SaveChanges();

            return RedirectToAction("ViewApplicants");
        }

        public ActionResult ViewSchools()
        {
            return View(db.Schools.OrderBy(s=>s.Name).ToList());
        }

        public ActionResult AddSchool()
        {
            return View();
        }

        [HttpPost]
        public ActionResult AddSchool(School school)
        {
            // Convert rates to multipliers
            school.GenderBalance /= 100;
            school.PovertyRate /= 100;

            // Validate
            ModelState.Clear();
            TryValidateModel(school);
            if (!ModelState.IsValid)
            {
                return View(school);
            }

            db.Schools.Add(school);
            db.SaveChanges();

            return RedirectToAction("ViewSchools");
        }

        public ActionResult DeleteSchool(int id)
        {
            var school = db.Schools.Find(id);
            if (school == null)
            {
                return HttpNotFound();
            }

            return View(school);
        }

        [HttpPost]
        public ActionResult DeleteSchool(School school)
        {
            var queriedSchool = db.Schools.Find(school.ID);
            if (queriedSchool == null)
            {
                return HttpNotFound();
            }

            db.Applieds.RemoveRange(queriedSchool.Applieds);
            db.Selecteds.RemoveRange(queriedSchool.Selecteds);
            db.Waitlisteds.RemoveRange(queriedSchool.Waitlisteds);
            db.Shuffleds.RemoveRange(queriedSchool.Shuffleds);
            db.Schools.Remove(queriedSchool);
            db.SaveChanges();

            return RedirectToAction("ViewSchools");
        }

        /*
         * ---------- HELPER METHODS ------------
         */

        private static School GetAllSchoolSchool()
        {
            var school = new School();
            school.ID = 0;
            school.Name = "All Schools";

            return school;
        }

        private DateTime? GetLotteryRunDate()
        {
            //return db.GlobalConfigs.First().LotteryRunDate; // real call
            return null; // forced lottery not run
            //return new DateTime(2014, 11, 21); // forced lottery run already
        }

        private DateTime? GetLotteryCloseDate()
        {
            //return db.GlobalConfigs.First().CloseDate; // real call
            return new DateTime(2014, 11, 20); // forced lottery closed
            //return new DateTime(2014, 11, 30); // forced lottery open
        }

        private bool WasLotteryRun()
        {
            return db.GlobalConfigs.First().LotteryRunDate != null;
        }

        private void AddSchoolsToViewBag()
        {
            var schools = db.Schools.OrderBy(s => s.Name).ToList();
            schools.Insert(0, ALL_SCHOOL_SCHOOL);
            ViewBag.Schools = schools;
        }

        private FileStreamResult ExportApplicantsCsvFile(IEnumerable<Applicant> applicants, string fileName)
        {
            var csvText = Utils.ApplicantsToCsv(applicants);

            var byteArray = Encoding.UTF8.GetBytes(csvText);
            var stream = new MemoryStream(byteArray);

            return File(stream, "text/plain", fileName);
        }

        private void PrepareEditApplicantView(Applicant applicant)
        {
            viewHelper.PrepareStudentInformationView(ViewBag, false);
            viewHelper.PrepareGuardianInformationView(ViewBag);
            viewHelper.PrepareSchoolSelectionView(ViewBag, applicant);
        }
    }
}