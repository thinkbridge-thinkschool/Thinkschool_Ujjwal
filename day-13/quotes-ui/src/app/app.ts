import { Component, inject, signal } from '@angular/core';
import { QuotesComponent } from './components/quotes/quotes';
import { QuoteFormComponent } from './components/quote-form/quote-form';
import { QuoteFormSignalComponent } from './components/quote-form-signal/quote-form-signal';
import { LoginComponent } from './components/login/login';
import { AuthService } from './services/auth';

type View = 'quotes' | 'create' | 'create-signal';

@Component({
  selector: 'app-root',
  imports: [QuotesComponent, QuoteFormComponent, QuoteFormSignalComponent, LoginComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(AuthService);

  protected readonly view = signal<View>('quotes');

  setView(view: View): void {
    this.view.set(view);
  }

  onQuoteCreated(): void {
    // Hand back to the list view - QuotesComponent re-mounts and refetches,
    // so the new quote shows up without threading state between the two.
    this.view.set('quotes');
  }
}
