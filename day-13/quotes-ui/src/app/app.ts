import { Component } from '@angular/core';
import { QuotesComponent } from './components/quotes/quotes';

@Component({
  selector: 'app-root',
  imports: [QuotesComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
